using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class SessionStartStaticCapture {
    private sealed record ReplayWindow(
        string Endpoint,
        string CaptureReason,
        string CallHomeEndpoint,
        bool AllowEmptyPackets);

    private static readonly IReadOnlyDictionary<string, ReplayWindow> Windows =
        new Dictionary<string, ReplayWindow>(StringComparer.OrdinalIgnoreCase) {
            ["session/start_1"] = new("session/start_1", "session/start", "session/start_1", AllowEmptyPackets: false),
            ["session/start_2"] = new("session/start_2", "session/start_1", "session/start_2", AllowEmptyPackets: false),
            ["session/start_3"] = new("session/start_3", "session/start_2", "session/start_3", AllowEmptyPackets: false),
            ["session/start_4"] = new("session/start_4", "session/start_3", "session/start_4", AllowEmptyPackets: true)
        };

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> LoadPackets(
        string endpoint,
        string logPath,
        SessionLogger logger) {
        if (!Windows.TryGetValue(endpoint.Trim(), out var window)) {
            throw new InvalidOperationException($"static capture is only available for session/start_1/session/start_2/session/start_3/session/start_4, not '{endpoint}'");
        }

        logger.WriteLine($"auth.session_start_static.capture.endpoint={window.Endpoint}");
        logger.WriteLine($"auth.session_start_static.capture.log_path={Path.GetFullPath(logPath)}");
        logger.WriteLine($"auth.session_start_static.capture.capture_reason={window.CaptureReason}");
        logger.WriteLine($"auth.session_start_static.capture.call_home_endpoint={window.CallHomeEndpoint}");

        var packets = new List<IReadOnlyDictionary<string, object?>>();
        var capturing = false;
        ushort? lastTxRawCommandId = null;
        var lineNumber = 0;

        foreach (var line in File.ReadLines(logPath)) {
            lineNumber++;

            if (StaticReplayCaptureSupport.TryParseBtCaptureStartReason(line, out var captureReason)) {
                if (capturing && !string.Equals(captureReason, window.CaptureReason, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                if (!capturing && string.Equals(captureReason, window.CaptureReason, StringComparison.OrdinalIgnoreCase)) {
                    capturing = true;
                    lastTxRawCommandId = null;
                }

                continue;
            }

            if (!capturing) {
                continue;
            }

            if (StaticReplayCaptureSupport.TryParseCallingHomeEndpoint(line, out var callHomeEndpoint)) {
                if (string.Equals(callHomeEndpoint, window.CallHomeEndpoint, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                continue;
            }

            if (!StaticReplayCaptureSupport.TryParseBtFrame(line, out var direction, out var frameHex)) {
                continue;
            }

            var frame = Convert.FromHexString(frameHex);
            if (direction == "tx") {
                lastTxRawCommandId = StaticReplayCaptureSupport.ReadRawCommandId(frame);
                continue;
            }

            if (lastTxRawCommandId is null) {
                logger.WriteLine($"auth.session_start_static.capture.warning=line_{lineNumber}_rx_without_tx={frameHex}");
                continue;
            }

            packets.Add(StaticReplayCaptureSupport.BuildReplayPacket(lastTxRawCommandId.Value, frame));
        }

        if (packets.Count == 0 && !window.AllowEmptyPackets) {
            throw new InvalidOperationException($"no captured packets found for {window.Endpoint}");
        }

        logger.WriteLine($"auth.session_start_static.capture.packet_count={packets.Count}");
        return packets;
    }
}
