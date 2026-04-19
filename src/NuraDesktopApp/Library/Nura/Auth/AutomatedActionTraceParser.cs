using System.Text.Json;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class AutomatedActionTraceParser {
    internal static AutomatedActionTraceSummary Parse(Dictionary<string, object?> responseBody) {
        if (!TryAsMap(GetMapValue(responseBody, "d"), out var dataMap)) {
            return AutomatedActionTraceSummary.Empty;
        }

        var actions = EnumerateActions(dataMap).ToArray();
        if (actions.Length == 0) {
            return AutomatedActionTraceSummary.Empty;
        }

        var appTriggers = new List<AutomatedAppTriggerTrace>();
        var callHomeEndpoints = new List<string>();
        var manualWaitTriggers = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in actions) {
            var type = GetString(GetMapValue(action, "t")) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type)) {
                continue;
            }

            counts[type] = counts.TryGetValue(type, out var existing) ? existing + 1 : 1;

            if (string.Equals(type, "t", StringComparison.OrdinalIgnoreCase)) {
                var trigger = GetString(GetMapValue(action, "c")) ?? string.Empty;
                var data = GetMapValue(action, "d");
                appTriggers.Add(new AutomatedAppTriggerTrace(
                    Trigger: trigger,
                    Data: data,
                    DataJson: JsonSerializer.Serialize(data)));
                continue;
            }

            if (string.Equals(type, "f", StringComparison.OrdinalIgnoreCase)) {
                var endpoint = GetString(GetMapValue(action, "e"));
                if (!string.IsNullOrWhiteSpace(endpoint)) {
                    callHomeEndpoints.Add(endpoint);
                }
                continue;
            }

            if (string.Equals(type, "m", StringComparison.OrdinalIgnoreCase)) {
                var trigger = GetString(GetMapValue(action, "c"));
                if (!string.IsNullOrWhiteSpace(trigger)) {
                    manualWaitTriggers.Add(trigger);
                }
            }
        }

        return new AutomatedActionTraceSummary(
            ActionCount: actions.Length,
            RunCount: GetCount(counts, "r"),
            WaitCount: GetCount(counts, "w"),
            EnhancedWaitCount: GetCount(counts, "W"),
            CallHomeCount: GetCount(counts, "f"),
            UnencryptedRunCount: GetCount(counts, "u"),
            AppEncryptedRunCount: GetCount(counts, "a"),
            AppTriggerCount: GetCount(counts, "t"),
            ManualWaitCount: GetCount(counts, "m"),
            AppEncryptedResponseRunCount: GetCount(counts, "R"),
            AppTriggers: appTriggers,
            CallHomeEndpoints: callHomeEndpoints,
            ManualWaitTriggers: manualWaitTriggers);
    }

    private static int GetCount(IReadOnlyDictionary<string, int> counts, string key) {
        return counts.TryGetValue(key, out var count) ? count : 0;
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
}

internal sealed record AutomatedActionTraceSummary(
    int ActionCount,
    int RunCount,
    int WaitCount,
    int EnhancedWaitCount,
    int CallHomeCount,
    int UnencryptedRunCount,
    int AppEncryptedRunCount,
    int AppTriggerCount,
    int ManualWaitCount,
    int AppEncryptedResponseRunCount,
    IReadOnlyList<AutomatedAppTriggerTrace> AppTriggers,
    IReadOnlyList<string> CallHomeEndpoints,
    IReadOnlyList<string> ManualWaitTriggers) {
    internal static readonly AutomatedActionTraceSummary Empty = new(
        ActionCount: 0,
        RunCount: 0,
        WaitCount: 0,
        EnhancedWaitCount: 0,
        CallHomeCount: 0,
        UnencryptedRunCount: 0,
        AppEncryptedRunCount: 0,
        AppTriggerCount: 0,
        ManualWaitCount: 0,
        AppEncryptedResponseRunCount: 0,
        AppTriggers: [],
        CallHomeEndpoints: [],
        ManualWaitTriggers: []);
}

internal sealed record AutomatedAppTriggerTrace(
    string Trigger,
    object? Data,
    string DataJson);
