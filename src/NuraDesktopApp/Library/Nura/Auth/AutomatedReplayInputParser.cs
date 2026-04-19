using System.Text.Json;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class AutomatedReplayInputParser {
    public static IReadOnlyDictionary<string, object?> ParsePayloadFile(string path) {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("payload json must be an object");
        }

        return ParseObject(document.RootElement);
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> ParsePacketsFile(string path) {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("packets", out var packetsProperty)) {
            root = packetsProperty;
        }

        if (root.ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("packets json must be an array or an object with a packets array");
        }

        var packets = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var element in root.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.Object) {
                throw new InvalidOperationException("each packet entry must be an object");
            }

            var packet = ParseObject(element);
            if (!packet.ContainsKey("b")) {
                if (packet.TryGetValue("b_hex", out var bHexValue) && bHexValue is string bHex) {
                    packet["b"] = Convert.FromHexString(NormalizeHex(bHex));
                    packet.Remove("b_hex");
                } else {
                    throw new InvalidOperationException("packet entry must contain b or b_hex");
                }
            } else if (packet["b"] is string bString) {
                packet["b"] = ParseByteString(bString);
            }

            packets.Add(packet);
        }

        return packets;
    }

    private static Dictionary<string, object?> ParseObject(JsonElement element) {
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) {
            map[property.Name] = ParseValue(property.Value);
        }

        return map;
    }

    private static object? ParseValue(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Null => null,
            JsonValueKind.False => false,
            JsonValueKind.True => true,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ParseNumber(element),
            JsonValueKind.Object => ParseObject(element),
            JsonValueKind.Array => ParseArray(element),
            _ => element.GetRawText()
        };
    }

    private static object ParseNumber(JsonElement element) {
        if (element.TryGetInt32(out var int32Value)) {
            return int32Value;
        }

        if (element.TryGetInt64(out var int64Value)) {
            return int64Value;
        }

        return element.GetDouble();
    }

    private static object ParseArray(JsonElement element) {
        var items = element.EnumerateArray().ToArray();
        if (items.All(item => item.ValueKind == JsonValueKind.Number && item.TryGetByte(out _))) {
            return items.Select(item => item.GetByte()).ToArray();
        }

        return items.Select(ParseValue).ToArray();
    }

    private static byte[] ParseByteString(string value) {
        var normalized = NormalizeHex(value);
        try {
            return Convert.FromHexString(normalized);
        } catch (FormatException) {
        }

        try {
            return Convert.FromBase64String(value);
        } catch (FormatException ex) {
            throw new InvalidOperationException("packet byte string must be hex or base64", ex);
        }
    }

    private static string NormalizeHex(string value) {
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? value[2..]
            : value;
    }
}
