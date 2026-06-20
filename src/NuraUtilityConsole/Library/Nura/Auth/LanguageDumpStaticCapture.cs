using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class LanguageDumpStaticCapture {
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> LoadPackets(
        string endpoint,
        string logPath,
        SessionLogger logger) {
        var normalizedEndpoint = endpoint.Trim();
        logger.WriteLine($"auth.language_dump_static.capture.endpoint={normalizedEndpoint}");
        logger.WriteLine($"auth.language_dump_static.capture.log_path={Path.GetFullPath(logPath)}");

        return normalizedEndpoint.ToLowerInvariant() switch {
            "change_language_1" => ParsePreparePacketsForChangeLanguage1(logPath, logger),
            "change_language_2" => ParsePacketsBetweenMarkers(logPath, "change_language_1", "change_language_2", logger),
            "change_language_3" => ParsePacketsBetweenMarkers(logPath, "change_language_2", "change_language_3", logger),
            _ => throw new InvalidOperationException($"static capture is only available for change_language_1/change_language_2/change_language_3, not '{endpoint}'")
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParsePreparePacketsForChangeLanguage1(
        string logPath,
        SessionLogger logger) {
        var packets = new List<IReadOnlyDictionary<string, object?>>();
        var capturing = false;
        ushort? lastTxRawCommandId = null;
        var lineNumber = 0;

        foreach (var line in File.ReadLines(logPath)) {
            lineNumber++;

            if (StaticReplayCaptureSupport.TryParseAutomatedEntryEndpoint(line, out var automatedEndpoint) &&
                string.Equals(automatedEndpoint, "change_language", StringComparison.OrdinalIgnoreCase)) {
                capturing = true;
                lastTxRawCommandId = null;
                packets.Clear();
                continue;
            }

            if (StaticReplayCaptureSupport.TryParseCallingHomeEndpoint(line, out var callingHomeEndpoint) &&
                capturing &&
                string.Equals(callingHomeEndpoint, "change_language_1", StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            if (!capturing) {
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
                logger.WriteLine($"auth.language_dump_static.capture.warning=line_{lineNumber}_prepare_rx_without_tx={frameHex}");
                continue;
            }

            packets.Add(StaticReplayCaptureSupport.BuildReplayPacket(lastTxRawCommandId.Value, frame));
        }

        if (packets.Count == 0) {
            throw new InvalidOperationException("no captured prepare packets found before change_language_1");
        }

        logger.WriteLine("auth.language_dump_static.capture.start_marker=change_language");
        logger.WriteLine("auth.language_dump_static.capture.end_marker=change_language_1");
        logger.WriteLine($"auth.language_dump_static.capture.packet_count={packets.Count}");
        return packets;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParsePacketsBetweenMarkers(
        string logPath,
        string startEndpoint,
        string endEndpoint,
        SessionLogger logger) {
        var packets = new List<IReadOnlyDictionary<string, object?>>();
        var capturing = false;
        ushort? lastTxRawCommandId = null;
        var lineNumber = 0;

        foreach (var line in File.ReadLines(logPath)) {
            lineNumber++;

            if (StaticReplayCaptureSupport.TryParseCallingHomeEndpoint(line, out var callingHomeEndpoint)) {
                if (capturing && string.Equals(callingHomeEndpoint, endEndpoint, StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                if (string.Equals(callingHomeEndpoint, startEndpoint, StringComparison.OrdinalIgnoreCase)) {
                    capturing = true;
                    lastTxRawCommandId = null;
                }

                continue;
            }

            if (!capturing) {
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
                logger.WriteLine($"auth.language_dump_static.capture.warning=line_{lineNumber}_rx_without_tx={frameHex}");
                continue;
            }

            packets.Add(StaticReplayCaptureSupport.BuildReplayPacket(lastTxRawCommandId.Value, frame));
        }

        if (packets.Count == 0) {
            throw new InvalidOperationException($"no captured packets found between {startEndpoint} and {endEndpoint}");
        }

        logger.WriteLine($"auth.language_dump_static.capture.start_marker={startEndpoint}");
        logger.WriteLine($"auth.language_dump_static.capture.end_marker={endEndpoint}");
        logger.WriteLine($"auth.language_dump_static.capture.packet_count={packets.Count}");
        return packets;
    }

}
