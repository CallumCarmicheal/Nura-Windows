using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;
using System.IO;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class SessionStartExecutionSupport {
    private static readonly HashSet<ushort> AuthenticatedResponseCommandIds = [
        0x0006, 0x1006,
        0x000A, 0x100A,
        0x0013, 0x1013,
        0x0008, 0x1008,
        0x000F, 0x100F,
        0x000C, 0x100C
    ];

    private static readonly HashSet<ushort> BulkResponseCommandIds = [
        0x1006, 0x100A, 0x1013,
        0x1007, 0x100B, 0x1014,
        0x1008, 0x100F, 0x100C,
        0x1009, 0x1010, 0x100D
    ];

    internal static async Task<List<IReadOnlyDictionary<string, object?>>> ExecuteLocalActionsAsync(
        SessionStartResponseDetails details,
        IHeadsetTransport transport,
        SessionLogger logger,
        byte unencryptedGaiaVersion,
        byte unencryptedGaiaFlags,
        byte runGaiaVersion,
        byte runGaiaFlags,
        int preRunCaptureIdleTimeoutMs,
        int preRunCaptureMaxFrames,
        CancellationToken cancellationToken) {
        var packets = new List<IReadOnlyDictionary<string, object?>>();

        packets.AddRange(await ExecuteUnencryptedPacketsAsync(
            details,
            transport,
            logger,
            unencryptedGaiaVersion,
            unencryptedGaiaFlags,
            cancellationToken));

        if (details.RunPackets.Count > 0 && preRunCaptureIdleTimeoutMs > 0 && preRunCaptureMaxFrames > 0) {
            logger.WriteLine($"auth.session_local.pre_run_capture.idle_timeout_ms={preRunCaptureIdleTimeoutMs}");
            logger.WriteLine($"auth.session_local.pre_run_capture.max_frames={preRunCaptureMaxFrames}");
            var extraFrames = await transport.CollectAsync(
                logger,
                preRunCaptureIdleTimeoutMs,
                preRunCaptureMaxFrames,
                cancellationToken);
            logger.WriteLine($"auth.session_local.pre_run_capture.frame_count={extraFrames.Count}");
            for (var index = 0; index < extraFrames.Count; index++) {
                var frame = extraFrames[index];
                logger.WriteLine($"auth.session_local.pre_run_capture.frame.{index}.command_raw=0x{frame.RawCommandId:x4}");
                logger.WriteLine($"auth.session_local.pre_run_capture.frame.{index}.data.hex={Hex.Format(frame.Data)}");
                logger.WriteLine($"auth.session_local.pre_run_capture.frame.{index}.payload_ex_status.hex={Hex.Format(frame.PayloadExcludingStatus)}");
            }
        }

        for (var index = 0; index < details.RunPackets.Count; index++) {
            var packet = details.RunPackets[index];
            if (packet.PayloadBytes is not { Length: > 0 }) {
                continue;
            }

            var rawCommandId = ResolveEntryServerEncryptedCommandId(packet.FlagA, packet.FlagM, appEncryptedResponse: false);
            var frame = GaiaPackets.BuildRawCommand(rawCommandId, packet.PayloadBytes, runGaiaVersion, runGaiaFlags);
            logger.WriteLine($"auth.session_local.r.{index}.request.command_raw=0x{rawCommandId:x4}");
            logger.WriteLine($"auth.session_local.r.{index}.request.flag_a={packet.FlagA}");
            logger.WriteLine($"auth.session_local.r.{index}.request.flag_m={packet.FlagM}");
            logger.WriteLine($"auth.session_local.r.{index}.request.gaia_version=0x{runGaiaVersion:x2}");
            logger.WriteLine($"auth.session_local.r.{index}.request.gaia_flags=0x{runGaiaFlags:x2}");
            logger.WriteLine($"auth.session_local.r.{index}.request.payload.hex={Hex.Format(packet.PayloadBytes)}");
            logger.WriteLine($"auth.session_local.r.{index}.request.payload.base64={Convert.ToBase64String(packet.PayloadBytes)}");

            GaiaResponse response;
            try {
                response = await transport.SendAsync(frame, logger, cancellationToken);
            } catch (IOException ex) {
                logger.WriteLine($"auth.session_local.r.{index}.transport_disconnect_after_send=true");
                logger.WriteLine($"auth.session_local.r.{index}.transition_hint=possible_headset_restart_or_reconnect");
                throw new SessionStartLocalTransitionException(index, rawCommandId, packet.PayloadBytes, ex);
            } catch (OperationCanceledException ex) {
                logger.WriteLine($"auth.session_local.r.{index}.response_timeout=true");
                logger.WriteLine($"auth.session_local.r.{index}.response_timeout_hint=no_frame_received_after_send");
                throw new SessionStartNoResponseException(index, rawCommandId, packet.PayloadBytes, ex);
            }

            var responseFlagA = AuthenticatedResponseCommandIds.Contains(response.CommandId);
            var responseFlagM = BulkResponseCommandIds.Contains(response.CommandId);
            var responsePayload = response.PayloadExcludingStatus;
            logger.WriteLine($"auth.session_local.r.{index}.response.command_raw=0x{response.RawCommandId:x4}");
            logger.WriteLine($"auth.session_local.r.{index}.response.command=0x{response.CommandId:x4}");
            logger.WriteLine($"auth.session_local.r.{index}.response.flag_a={responseFlagA}");
            logger.WriteLine($"auth.session_local.r.{index}.response.flag_m={responseFlagM}");
            logger.WriteLine($"auth.session_local.r.{index}.response.payload_ex_status.hex={Hex.Format(responsePayload)}");
            logger.WriteLine($"auth.session_local.r.{index}.response.payload_ex_status.base64={Convert.ToBase64String(responsePayload)}");

            packets.Add(new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["e"] = true,
                ["a"] = responseFlagA,
                ["b"] = responsePayload,
                ["m"] = responseFlagM
            });
        }

        return packets;
    }

    internal static async Task<List<IReadOnlyDictionary<string, object?>>> ExecuteUnencryptedPacketsAsync(
        SessionStartResponseDetails details,
        IHeadsetTransport transport,
        SessionLogger logger,
        byte gaiaVersion,
        byte gaiaFlags,
        CancellationToken cancellationToken) {
        var packets = new List<IReadOnlyDictionary<string, object?>>();

        for (var index = 0; index < details.Packets.Count; index++) {
            var packet = details.Packets[index];
            if (packet.PayloadBytes is not { Length: > 0 }) {
                continue;
            }

            var frame = RawPacketSupport.BuildFrameFromPacketBytes(packet.PayloadBytes, out var mode, gaiaVersion, gaiaFlags);
            logger.WriteLine($"auth.session_local.u.{index}.mode={mode}");
            logger.WriteLine($"auth.session_local.u.{index}.request.gaia_version=0x{gaiaVersion:x2}");
            logger.WriteLine($"auth.session_local.u.{index}.request.gaia_flags=0x{gaiaFlags:x2}");
            logger.WriteLine($"auth.session_local.u.{index}.request.hex={Hex.Format(packet.PayloadBytes)}");
            logger.WriteLine($"auth.session_local.u.{index}.request.base64={Convert.ToBase64String(packet.PayloadBytes)}");
            var response = await transport.SendAsync(frame, logger, cancellationToken);
            logger.WriteLine($"auth.session_local.u.{index}.response.command=0x{response.CommandId:x4}");
            logger.WriteLine($"auth.session_local.u.{index}.response.data.hex={Hex.Format(response.Data)}");
            logger.WriteLine($"auth.session_local.u.{index}.response.payload_ex_status.hex={Hex.Format(response.PayloadExcludingStatus)}");

            packets.Add(new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["e"] = false,
                ["a"] = false,
                ["b"] = response.Data,
                ["m"] = false
            });
        }

        return packets;
    }

    internal static void LogParsedDetails(string prefix, SessionStartResponseDetails details, SessionLogger logger) {
        if (details.NumberValue is { } numberValue) {
            logger.WriteLine($"{prefix}.number={numberValue}");
        }

        if (details.SessionId is { } sessionId) {
            logger.WriteLine($"{prefix}.session_id={sessionId}");
        }

        if (details.P1 is { } p1) {
            logger.WriteLine($"{prefix}.p1={p1:0.###}");
        }

        if (details.P2 is { } p2) {
            logger.WriteLine($"{prefix}.p2={p2:0.###}");
        }

        if (!string.IsNullOrWhiteSpace(details.FinalEvent)) {
            logger.WriteLine($"{prefix}.final_event={details.FinalEvent}");
        }

        for (var index = 0; index < details.Packets.Count; index++) {
            LogPacket($"{prefix}.packet.{index}", details.Packets[index], logger);
        }

        for (var index = 0; index < details.RunPackets.Count; index++) {
            LogPacket($"{prefix}.run_packet.{index}", details.RunPackets[index], logger);
        }
    }

    private static void LogPacket(string prefix, SessionStartPacket packet, SessionLogger logger) {
        logger.WriteLine($"{prefix}.flag_e={packet.FlagE}");
        logger.WriteLine($"{prefix}.flag_a={packet.FlagA}");
        logger.WriteLine($"{prefix}.flag_m={packet.FlagM}");
        if (!string.IsNullOrWhiteSpace(packet.Base64Payload)) {
            logger.WriteLine($"{prefix}.base64={packet.Base64Payload}");
        }

        if (packet.PayloadBytes is { Length: > 0 }) {
            logger.WriteLine($"{prefix}.hex={Convert.ToHexString(packet.PayloadBytes).ToLowerInvariant()}");
        }
    }

    private static ushort ResolveEntryServerEncryptedCommandId(bool flagA, bool flagM, bool appEncryptedResponse) {
        return (flagA, flagM, appEncryptedResponse) switch {
            // Match the Android GAIACommandID families observed in real app-run logs.
            // The start_3 ActionRun packets are sent as EntryServerEncrypted* commands
            // (0x0008/0x0009/0x000F/0x0010 and bulk variants), not the
            // ResponseAppEncryptedFromServerEncrypted family we tried earlier.
            (false, false, false) => 0x0009,
            (false, false, true) => 0x0010,
            (false, true, false) => 0x1009,
            (false, true, true) => 0x1010,
            (true, false, false) => 0x0008,
            (true, false, true) => 0x000F,
            (true, true, false) => 0x1008,
            (true, true, true) => 0x100F
        };
    }
}

internal sealed class SessionStartLocalTransitionException : IOException {
    internal SessionStartLocalTransitionException(int packetIndex, ushort rawCommandId, byte[] requestPayload, IOException innerException)
        : base($"session/start local packet {packetIndex} (command 0x{rawCommandId:x4}) likely triggered a disconnect/reconnect transition", innerException) {
        PacketIndex = packetIndex;
        RawCommandId = rawCommandId;
        RequestPayload = requestPayload;
    }

    internal int PacketIndex { get; }
    internal ushort RawCommandId { get; }
    internal byte[] RequestPayload { get; }
}

internal sealed class SessionStartNoResponseException : TimeoutException {
    internal SessionStartNoResponseException(int packetIndex, ushort rawCommandId, byte[] requestPayload, OperationCanceledException innerException)
        : base($"session/start local packet {packetIndex} (command 0x{rawCommandId:x4}) produced no response frame before timeout", innerException) {
        PacketIndex = packetIndex;
        RawCommandId = rawCommandId;
        RequestPayload = requestPayload;
    }

    internal int PacketIndex { get; }
    internal ushort RawCommandId { get; }
    internal byte[] RequestPayload { get; }
}
