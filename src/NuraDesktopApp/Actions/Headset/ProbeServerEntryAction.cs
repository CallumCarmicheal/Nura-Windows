using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionProbeServerEntry : IAction {
    private static readonly ushort[] DefaultCommandIds = [0x0010, 0x001e, 0x0012, 0x0020];

    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        var payload = ResolvePayload(args, logger);
        var commandIds = ParseCommandIds(args);
        var readTimeoutMs = ParseReadTimeoutMs(args);
        var captureAllFrames = ArgumentReader.HasFlag(args, "--capture-all-frames");
        var maxFrames = ParseMaxFrames(args);
        var overallTimeoutMs = ParseOverallTimeoutMs(args, readTimeoutMs, maxFrames, captureAllFrames);
        var gaiaVersion = ParseByteArgument(args, "--gaia-version", 0x01);
        var gaiaFlags = ParseByteArgument(args, "--gaia-flags", 0x00);

        logger.WriteLine($"probe.server_entry.payload.hex={Hex.Format(payload)}");
        logger.WriteLine($"probe.server_entry.payload.base64={Convert.ToBase64String(payload)}");
        logger.WriteLine($"probe.server_entry.commands={string.Join(",", commandIds.Select(id => $"0x{id:x4}"))}");
        logger.WriteLine($"probe.server_entry.read_timeout_ms={readTimeoutMs}");
        logger.WriteLine($"probe.server_entry.overall_timeout_ms={overallTimeoutMs}");
        logger.WriteLine($"probe.server_entry.capture_all_frames={captureAllFrames}");
        logger.WriteLine($"probe.server_entry.max_frames={maxFrames}");
        logger.WriteLine($"probe.server_entry.gaia_version=0x{gaiaVersion:x2}");
        logger.WriteLine($"probe.server_entry.gaia_flags=0x{gaiaFlags:x2}");

        var successCount = 0;
        for (var index = 0; index < commandIds.Count; index++) {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(overallTimeoutMs));
            var rawCommandId = commandIds[index];
            var frame = GaiaPackets.BuildRawCommand(rawCommandId, payload, gaiaVersion, gaiaFlags);
            logger.WriteLine($"probe.server_entry.{index}.request.command_raw=0x{rawCommandId:x4}");
            logger.WriteLine($"probe.server_entry.{index}.request.frame.hex={Hex.Format(frame.Bytes)}");

            try {
                await using IHeadsetTransport transport = new RfcommHeadsetTransport();
                logger.WriteLine($"probe.server_entry.{index}.transport={transport.Describe()}");
                logger.WriteLine($"probe.server_entry.{index}.connecting=true");
                await transport.ConnectAsync(config.DeviceAddress, cts.Token);
                logger.WriteLine($"probe.server_entry.{index}.connected=true");

                if (captureAllFrames) {
                    var responses = await transport.SendAndCollectAsync(frame, logger, readTimeoutMs, maxFrames, cts.Token);
                    logger.WriteLine($"probe.server_entry.{index}.response_count={responses.Count}");
                    for (var responseIndex = 0; responseIndex < responses.Count; responseIndex++) {
                        var response = responses[responseIndex];
                        logger.WriteLine($"probe.server_entry.{index}.response.{responseIndex}.command=0x{response.CommandId:x4}");
                        logger.WriteLine($"probe.server_entry.{index}.response.{responseIndex}.data.hex={Hex.Format(response.Data)}");
                        logger.WriteLine($"probe.server_entry.{index}.response.{responseIndex}.data.base64={Convert.ToBase64String(response.Data)}");
                        logger.WriteLine($"probe.server_entry.{index}.response.{responseIndex}.payload_ex_status.hex={Hex.Format(response.PayloadExcludingStatus)}");
                        logger.WriteLine($"probe.server_entry.{index}.response.{responseIndex}.status=0x{response.Status:x2}");
                    }

                    if (responses.Any(response => response.Status == 0)) {
                        logger.WriteLine($"probe.server_entry.{index}.result=success");
                        successCount++;
                    } else if (responses.Count > 0) {
                        logger.WriteLine($"probe.server_entry.{index}.result=error_status");
                    } else {
                        logger.WriteLine($"probe.server_entry.{index}.result=no_frames");
                    }
                } else {
                    var response = await transport.SendAsync(frame, logger, cts.Token);
                    logger.WriteLine($"probe.server_entry.{index}.response.command=0x{response.CommandId:x4}");
                    logger.WriteLine($"probe.server_entry.{index}.response.data.hex={Hex.Format(response.Data)}");
                    logger.WriteLine($"probe.server_entry.{index}.response.data.base64={Convert.ToBase64String(response.Data)}");
                    logger.WriteLine($"probe.server_entry.{index}.response.payload_ex_status.hex={Hex.Format(response.PayloadExcludingStatus)}");
                    logger.WriteLine($"probe.server_entry.{index}.response.status=0x{response.Status:x2}");
                    if (response.Status == 0) {
                        logger.WriteLine($"probe.server_entry.{index}.result=success");
                        successCount++;
                    } else {
                        logger.WriteLine($"probe.server_entry.{index}.result=error_status");
                    }
                }
            } catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException) {
                logger.WriteLine($"probe.server_entry.{index}.result=transport_error");
                logger.WriteLine($"probe.server_entry.{index}.error={ex.GetType().Name}: {ex.Message}");
            }
        }

        logger.WriteLine($"probe.server_entry.success_count={successCount}");
        return successCount > 0 ? 0 : 1;
    }

    private static byte[] ResolvePayload(string[] args, SessionLogger logger) {
        var payloadHex = ArgumentReader.OptionalValue(args, "--payload-hex");
        if (!string.IsNullOrWhiteSpace(payloadHex)) {
            return Hex.Parse(payloadHex);
        }

        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        var details = SessionStartResponseParser.Parse(state.LastResponseBody ?? [])
            ?? throw new InvalidOperationException("last auth response does not contain a parsed session/start continuation");
        var packet = details.RunPackets.FirstOrDefault(runPacket => runPacket.PayloadBytes is { Length: > 0 })
            ?? throw new InvalidOperationException("last auth response does not contain any run packets with payload bytes");
        return packet.PayloadBytes!;
    }

    private static IReadOnlyList<ushort> ParseCommandIds(string[] args) {
        var raw = ArgumentReader.OptionalValue(args, "--command-raw-list");
        if (string.IsNullOrWhiteSpace(raw)) {
            return DefaultCommandIds;
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseCommandId)
            .Distinct()
            .ToArray();
    }

    private static ushort ParseCommandId(string raw) {
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
            return Convert.ToUInt16(raw[2..], 16);
        }

        return raw.All(Uri.IsHexDigit)
            ? Convert.ToUInt16(raw, 16)
            : Convert.ToUInt16(raw);
    }

    private static int ParseReadTimeoutMs(string[] args) {
        var raw = ArgumentReader.OptionalValue(args, "--read-timeout-ms");
        return raw is null ? 5000 : int.Parse(raw);
    }

    private static int ParseMaxFrames(string[] args) {
        var raw = ArgumentReader.OptionalValue(args, "--max-frames");
        return raw is null ? 8 : int.Parse(raw);
    }

    private static int ParseOverallTimeoutMs(string[] args, int readTimeoutMs, int maxFrames, bool captureAllFrames) {
        var raw = ArgumentReader.OptionalValue(args, "--overall-timeout-ms");
        if (raw is not null) {
            return int.Parse(raw);
        }

        if (!captureAllFrames) {
            return readTimeoutMs;
        }

        return Math.Max(readTimeoutMs + 2000, (readTimeoutMs * maxFrames) + 2000);
    }

    private static byte ParseByteArgument(string[] args, string flag, byte defaultValue) {
        var raw = ArgumentReader.OptionalValue(args, flag);
        if (string.IsNullOrWhiteSpace(raw)) {
            return defaultValue;
        }

        return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToByte(raw[2..], 16)
            : Convert.ToByte(raw);
    }
}
