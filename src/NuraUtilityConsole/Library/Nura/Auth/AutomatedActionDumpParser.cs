using System.Text.Json;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class AutomatedActionDumpParser {
    internal static AutomatedActionDump Parse(Dictionary<string, object?> responseBody) {
        if (!TryAsMap(GetMapValue(responseBody, "d"), out var dataMap)) {
            return AutomatedActionDump.Empty;
        }

        var actions = EnumerateActions(dataMap)
            .Select((action, index) => ParseAction(action, index))
            .ToArray();

        var packets = actions
            .SelectMany(action => action.Packets)
            .ToArray();

        return new AutomatedActionDump(actions, packets);
    }

    private static AutomatedActionDumpAction ParseAction(Dictionary<string, object?> action, int actionIndex) {
        var type = GetString(GetMapValue(action, "t")) ?? string.Empty;
        var packets = new List<AutomatedActionDumpPacket>();

        if (string.Equals(type, "r", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "u", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "a", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type, "R", StringComparison.OrdinalIgnoreCase)) {
            var packetIndex = 0;
            foreach (var packetNode in EnumerateArray(GetMapValue(action, "p"))) {
                var packet = ParsePacket(packetNode, actionIndex, packetIndex++, "packet", type);
                if (packet is not null) {
                    packets.Add(packet);
                }
            }
        } else if (string.Equals(type, "w", StringComparison.OrdinalIgnoreCase)) {
            var packet = ParsePacket(GetMapValue(action, "req"), actionIndex, 0, "request", type);
            if (packet is not null) {
                packets.Add(packet);
            }
        } else if (string.Equals(type, "W", StringComparison.OrdinalIgnoreCase)) {
            var packet = ParsePacket(GetMapValue(action, "r"), actionIndex, 0, "request", type);
            if (packet is not null) {
                packets.Add(packet);
            }
        }

        return new AutomatedActionDumpAction(
            ActionIndex: actionIndex,
            Type: type,
            Json: JsonSerializer.Serialize(action),
            Packets: packets);
    }

    private static AutomatedActionDumpPacket? ParsePacket(
        object? packetNode,
        int actionIndex,
        int packetIndex,
        string role,
        string actionType) {
        if (!TryAsMap(packetNode, out var packetMap)) {
            return null;
        }

        var binary = GetBytes(GetMapValue(packetMap, "b"));
        if (binary.Length == 0) {
            return null;
        }

        return new AutomatedActionDumpPacket(
            ActionIndex: actionIndex,
            PacketIndex: packetIndex,
            Role: role,
            ActionType: actionType,
            Encrypted: GetBool(GetMapValue(packetMap, "e")),
            Authenticated: GetBool(GetMapValue(packetMap, "a")),
            Binary: binary,
            BinaryHex: Convert.ToHexString(binary).ToLowerInvariant(),
            Json: JsonSerializer.Serialize(packetMap));
    }

    private static IEnumerable<Dictionary<string, object?>> EnumerateActions(Dictionary<string, object?> dataMap) {
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

    private static string? GetString(object? node) {
        return node switch {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
            _ => null
        };
    }

    private static bool GetBool(object? node) {
        return node switch {
            bool value => value,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => false
        };
    }

    private static byte[] GetBytes(object? node) {
        return node switch {
            byte[] bytes => bytes,
            JsonElement { ValueKind: JsonValueKind.String } jsonString => TryDecodeBase64(jsonString.GetString()),
            JsonElement { ValueKind: JsonValueKind.Array } jsonArray => jsonArray
                .EnumerateArray()
                .Select(element => checked((byte)element.GetInt32()))
                .ToArray(),
            List<object?> list => list
                .Select(item => item switch {
                    byte b => b,
                    sbyte sb => checked((byte)sb),
                    short s => checked((byte)s),
                    ushort us => checked((byte)us),
                    int i => checked((byte)i),
                    long l => checked((byte)l),
                    JsonElement value => checked((byte)value.GetInt32()),
                    _ => throw new InvalidOperationException($"unsupported packet byte value type: {item?.GetType().FullName ?? "null"}")
                })
                .ToArray(),
            _ => []
        };
    }

    private static byte[] TryDecodeBase64(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return [];
        }

        try {
            return Convert.FromBase64String(value);
        } catch (FormatException) {
            return [];
        }
    }
}

internal sealed record AutomatedActionDump(
    IReadOnlyList<AutomatedActionDumpAction> Actions,
    IReadOnlyList<AutomatedActionDumpPacket> Packets) {
    internal static readonly AutomatedActionDump Empty = new([], []);
}

internal sealed record AutomatedActionDumpAction(
    int ActionIndex,
    string Type,
    string Json,
    IReadOnlyList<AutomatedActionDumpPacket> Packets);

internal sealed record AutomatedActionDumpPacket(
    int ActionIndex,
    int PacketIndex,
    string Role,
    string ActionType,
    bool Encrypted,
    bool Authenticated,
    byte[] Binary,
    string BinaryHex,
    string Json);
