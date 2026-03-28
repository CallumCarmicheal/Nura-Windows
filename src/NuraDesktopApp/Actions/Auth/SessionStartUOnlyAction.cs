using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSessionStartUOnly : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(ParseReadTimeoutMs(args)));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        var details = SessionStartResponseParser.Parse(state.LastResponseBody ?? [])
            ?? throw new InvalidOperationException("last auth response does not contain a parsed session/start continuation");
        var config = LocalStateFiles.LoadConfig(logger);
        var gaiaVersion = ParseByteArgument(args, "--gaia-version", 0x01);
        var gaiaFlags = ParseByteArgument(args, "--gaia-flags", 0x00);

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.session_u_only.gaia_version=0x{gaiaVersion:x2}");
        logger.WriteLine($"auth.session_u_only.gaia_flags=0x{gaiaFlags:x2}");
        SessionStartExecutionSupport.LogParsedDetails("auth.session_u_only.source", details, logger);
        AuthStateSupport.LogSessionState(state, logger);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> packets;
        await using (IHeadsetTransport transport = new RfcommHeadsetTransport()) {
            logger.WriteLine($"transport={transport.Describe()}");
            logger.WriteLine("auth.session_u_only.connecting=true");
            await transport.ConnectAsync(config.DeviceAddress, cts.Token);
            logger.WriteLine("auth.session_u_only.connected=true");
            packets = await SessionStartExecutionSupport.ExecuteUnencryptedPacketsAsync(details, transport, logger, gaiaVersion, gaiaFlags, cts.Token);
        }

        logger.WriteLine($"auth.session_u_only.completed=true");
        logger.WriteLine($"auth.session_u_only.return_packets={packets.Count}");
        for (var index = 0; index < packets.Count; index++) {
            if (packets[index].TryGetValue("b", out var value) && value is byte[] bytes) {
                logger.WriteLine($"auth.session_u_only.return_packet.{index}.base64={Convert.ToBase64String(bytes)}");
                logger.WriteLine($"auth.session_u_only.return_packet.{index}.hex={Convert.ToHexString(bytes).ToLowerInvariant()}");
            }
        }

        if (details.RunPackets.Count > 0 && details.RunPackets[0].PayloadBytes is { Length: > 0 } nextRunPayload) {
            logger.WriteLine("auth.session_u_only.next_run_packet_index=0");
            logger.WriteLine("auth.session_u_only.next_run_packet.command_raw=0x0010");
            logger.WriteLine($"auth.session_u_only.next_run_packet.payload.hex={Convert.ToHexString(nextRunPayload).ToLowerInvariant()}");
            logger.WriteLine($"auth.session_u_only.next_run_packet.payload.base64={Convert.ToBase64String(nextRunPayload)}");
        }

        return 0;
    }

    private static int ParseReadTimeoutMs(string[] args) {
        var raw = ArgumentReader.OptionalValue(args, "--read-timeout-ms");
        return raw is null ? 10000 : int.Parse(raw);
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
