using System.Text.Json;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class SessionStartResponseParser {
    internal static SessionStartResponseDetails? Parse(Dictionary<string, object?> responseBody) {
        if (!TryAsMap(GetMapValue(responseBody, "d"), out var dataMap)) {
            return null;
        }

        var details = new SessionStartResponseDetails {
            NumberValue = GetIntFromTypedAction(dataMap, "number"),
            SessionId = GetIntFromTypedAction(dataMap, "session"),
            FinalEvent = GetFinalEvent(dataMap),
            P1 = GetDouble(GetMapValue(dataMap, "p1")),
            P2 = GetDouble(GetMapValue(dataMap, "p2"))
        };

        foreach (var packet in GetPacketsByActionType(dataMap, "u")) {
            details.Packets.Add(packet);
        }

        foreach (var packet in GetPacketsByActionType(dataMap, "r")) {
            details.RunPackets.Add(packet);
        }

        return details;
    }

    private static int? GetIntFromTypedAction(Dictionary<string, object?> dataMap, string category) {
        foreach (var action in EnumerateActionMaps(dataMap)) {
            if (!string.Equals(GetString(GetMapValue(action, "t")), "t", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!string.Equals(GetString(GetMapValue(action, "c")), category, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return GetInt(GetMapValue(action, "d"));
        }

        return null;
    }

    private static string? GetFinalEvent(Dictionary<string, object?> dataMap) {
        foreach (var action in EnumerateActionMaps(dataMap)) {
            if (!string.Equals(GetString(GetMapValue(action, "t")), "f", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var value = GetString(GetMapValue(action, "e"));
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }

    private static IEnumerable<SessionStartPacket> GetPacketsByActionType(Dictionary<string, object?> dataMap, string actionType) {
        foreach (var action in EnumerateActionMaps(dataMap)) {
            if (!string.Equals(GetString(GetMapValue(action, "t")), actionType, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            foreach (var packetNode in EnumerateArray(GetMapValue(action, "p"))) {
                if (!TryAsMap(packetNode, out var packetMap)) {
                    continue;
                }

                var payloadNode = GetMapValue(packetMap, "b");
                byte[]? bytes = GetBytes(payloadNode);
                var base64 = GetBase64(payloadNode, bytes);

                yield return new SessionStartPacket(
                    FlagE: GetBool(GetMapValue(packetMap, "e")) ?? false,
                    FlagA: GetBool(GetMapValue(packetMap, "a")) ?? false,
                    FlagM: GetBool(GetMapValue(packetMap, "m")) ?? false,
                    Base64Payload: base64,
                    PayloadBytes: bytes);
            }
        }
    }

    private static IEnumerable<Dictionary<string, object?>> EnumerateActionMaps(Dictionary<string, object?> dataMap) {
        foreach (var item in EnumerateArray(GetMapValue(dataMap, "a"))) {
            if (TryAsMap(item, out var map)) {
                yield return map;
            }
        }
    }

    private static IEnumerable<object?> EnumerateArray(object? node) {
        return node switch {
            List<object?> list => list,
            JsonElement { ValueKind: JsonValueKind.Array } jsonArray => JsonSerializer.Deserialize<List<object?>>(jsonArray.GetRawText()) ?? [],
            _ => []
        };
    }

    private static object? GetMapValue(Dictionary<string, object?> map, string key) {
        foreach (var entry in map) {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return entry.Value;
            }
        }

        return null;
    }

    private static bool TryAsMap(object? node, out Dictionary<string, object?> map) {
        switch (node) {
        case Dictionary<string, object?> directMap:
            map = directMap;
            return true;
        case JsonElement { ValueKind: JsonValueKind.Object } jsonObject:
            map = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonObject.GetRawText()) ?? [];
            return true;
        default:
            map = [];
            return false;
        }
    }

    private static int? GetInt(object? node) {
        return node switch {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetInt32(out var value) => value,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when int.TryParse(jsonString.GetString(), out var value) => value,
            _ => null
        };
    }

    private static double? GetDouble(object? node) {
        return node switch {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            long longValue => longValue,
            int intValue => intValue,
            JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetDouble(out var value) => value,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when double.TryParse(jsonString.GetString(), out var value) => value,
            _ => null
        };
    }

    private static bool? GetBool(object? node) {
        return node switch {
            bool boolValue => boolValue,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when bool.TryParse(jsonString.GetString(), out var value) => value,
            _ => null
        };
    }

    private static byte[]? GetBytes(object? node) {
        return node switch {
            byte[] bytes => bytes,
            JsonElement { ValueKind: JsonValueKind.String } jsonString => TryDecodeBase64(jsonString.GetString()),
            string stringValue => TryDecodeBase64(stringValue),
            _ => null
        };
    }

    private static string? GetBase64(object? node, byte[]? bytes) {
        return node switch {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
            byte[] rawBytes when rawBytes.Length > 0 => Convert.ToBase64String(rawBytes),
            _ when bytes is { Length: > 0 } => Convert.ToBase64String(bytes),
            _ => null
        };
    }

    private static byte[]? TryDecodeBase64(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        try {
            return Convert.FromBase64String(value);
        } catch (FormatException) {
            return null;
        }
    }

    private static string? GetString(object? node) {
        return node switch {
            string stringValue => stringValue,
            JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
            _ => null
        };
    }
}

internal sealed class SessionStartResponseDetails {
    internal int? NumberValue { get; init; }

    internal int? SessionId { get; init; }

    internal string? FinalEvent { get; init; }

    internal double? P1 { get; init; }

    internal double? P2 { get; init; }

    internal List<SessionStartPacket> Packets { get; } = [];

    internal List<SessionStartPacket> RunPackets { get; } = [];
}

internal sealed record SessionStartPacket(
    bool FlagE,
    bool FlagA,
    bool FlagM,
    string? Base64Payload,
    byte[]? PayloadBytes);
