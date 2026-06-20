using System.Text.RegularExpressions;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class StaticReplayCaptureSupport {
    internal static readonly Regex CallingHomeRegex = new(@"\[net454\.logger\.i\] Calling home \(endpoint: ([^)]+)\)\.\.\.", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex BtCaptureStartRegex = new(@"\[net454\] bt capture start reason=([^\s]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex BtFrameRegex = new(@"\[net454\.bt\.(tx|rx)\] len=\d+ hex=([0-9a-fA-F]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal static readonly Regex AutomatedEntryRegex = new(@"\[net454\.logger\.i\] Automated entry, endpoint: ([^,\s]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<ushort> AuthenticatedEncryptedRequestCommands = [
        0x0008, 0x000f,
        0x1008, 0x100f
    ];

    private static readonly HashSet<ushort> UnauthenticatedEncryptedRequestCommands = [
        0x0009, 0x0010,
        0x1009, 0x1010
    ];

    internal static bool TryParseCallingHomeEndpoint(string line, out string endpoint) {
        var match = CallingHomeRegex.Match(line);
        if (!match.Success) {
            endpoint = string.Empty;
            return false;
        }

        endpoint = match.Groups[1].Value.Trim();
        return true;
    }

    internal static bool TryParseBtCaptureStartReason(string line, out string reason) {
        var match = BtCaptureStartRegex.Match(line);
        if (!match.Success) {
            reason = string.Empty;
            return false;
        }

        reason = match.Groups[1].Value.Trim();
        return true;
    }

    internal static bool TryParseAutomatedEntryEndpoint(string line, out string endpoint) {
        var match = AutomatedEntryRegex.Match(line);
        if (!match.Success) {
            endpoint = string.Empty;
            return false;
        }

        endpoint = match.Groups[1].Value.Trim();
        return true;
    }

    internal static bool TryParseBtFrame(string line, out string direction, out string hex) {
        var match = BtFrameRegex.Match(line);
        if (!match.Success) {
            direction = string.Empty;
            hex = string.Empty;
            return false;
        }

        direction = match.Groups[1].Value;
        hex = match.Groups[2].Value;
        return true;
    }

    internal static IReadOnlyDictionary<string, object?> BuildReplayPacket(ushort requestRawCommandId, byte[] responseFrame) {
        var encrypted = IsEncryptedRequestCommand(requestRawCommandId);
        var authenticated = IsAuthenticatedRequestCommand(requestRawCommandId);
        var payload = ExtractReplayPayload(responseFrame, encrypted);

        return new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["e"] = encrypted,
            ["a"] = authenticated,
            ["b"] = payload
        };
    }

    internal static IReadOnlyDictionary<string, object?> BuildPacket(bool encrypted, bool authenticated, string hexPayload) {
        return new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["e"] = encrypted,
            ["a"] = authenticated,
            ["b"] = Convert.FromHexString(hexPayload)
        };
    }

    internal static ushort ReadRawCommandId(byte[] frame) {
        if (frame.Length < 8) {
            throw new InvalidOperationException("frame too short to contain a raw command id");
        }

        return (ushort)((frame[6] << 8) | frame[7]);
    }

    private static byte[] ExtractReplayPayload(byte[] responseFrame, bool encrypted) {
        var payloadOffset = encrypted ? 9 : 4;
        if (responseFrame.Length < payloadOffset) {
            throw new InvalidOperationException("response frame too short to extract replay payload");
        }

        return responseFrame[payloadOffset..];
    }

    private static bool IsEncryptedRequestCommand(ushort rawCommandId) {
        return AuthenticatedEncryptedRequestCommands.Contains(rawCommandId) ||
               UnauthenticatedEncryptedRequestCommands.Contains(rawCommandId);
    }

    private static bool IsAuthenticatedRequestCommand(ushort rawCommandId) {
        return AuthenticatedEncryptedRequestCommands.Contains(rawCommandId);
    }
}
