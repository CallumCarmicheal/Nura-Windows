using System.Text.Json;

namespace NuraLib.Auth;

internal static class NuraAuthResponseParser {
    public static NuraAuthSessionSnapshot ExtractSessionState(Dictionary<string, object?>? responseBody) {
        if (responseBody is null) {
            return NuraAuthSessionSnapshot.Empty;
        }

        var userSessionId =
            FindIntInNamedNode(responseBody, "user_session_status", "id", "user_session_id", "userSessionId", "session_id", "sessionId", "usid") ??
            FindIntInNamedNode(responseBody, "userSessionStatus", "id", "user_session_id", "userSessionId", "session_id", "sessionId", "usid") ??
            FindIntInNamedNode(responseBody, "user_session", "id", "user_session_id", "userSessionId", "session_id", "sessionId", "usid") ??
            FindIntInNamedNode(responseBody, "userSession", "id", "user_session_id", "userSessionId", "session_id", "sessionId", "usid") ??
            FindIntByKeyInPayload(responseBody, "user_session_id", "userSessionId", "sessionId", "usid");

        var appSessionId =
            FindIntInNamedNode(responseBody, "app_session_status", "id", "app_session_id", "appSessionId", "session_id", "sessionId", "asid") ??
            FindIntInNamedNode(responseBody, "appSessionStatus", "id", "app_session_id", "appSessionId", "session_id", "sessionId", "asid") ??
            FindIntInNamedNode(responseBody, "app_session", "id", "app_session_id", "appSessionId", "session_id", "sessionId", "asid") ??
            FindIntInNamedNode(responseBody, "appSession", "id", "app_session_id", "appSessionId", "session_id", "sessionId", "asid") ??
            FindIntByKeyInPayload(responseBody, "app_session_id", "appSessionId", "asid");

        var bluetoothSessionId =
            FindIntInNamedNode(responseBody, "bluetooth_session_status", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntInNamedNode(responseBody, "bluetoothSessionStatus", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntInNamedNode(responseBody, "bluetooth_session", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntInNamedNode(responseBody, "bluetoothSession", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntByKeyInPayload(responseBody, "bluetooth_session_id", "bluetoothSessionId", "bsid");

        var appSessionToken =
            FindStringInNamedNode(responseBody, "app_session_status", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringInNamedNode(responseBody, "appSessionStatus", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringInNamedNode(responseBody, "app_session", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringInNamedNode(responseBody, "appSession", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringByKeyInPayload(responseBody, "app_session_token", "appSessionToken", "a");

        var userSessionStatus =
            FindStringInNamedNode(responseBody, "user_session_status", "status", "type", "state") ??
            FindStringInNamedNode(responseBody, "userSessionStatus", "status", "type", "state") ??
            FindStringByKeyInPayload(responseBody, "user_session_status", "userSessionStatus");

        var appSessionStatus =
            FindStringInNamedNode(responseBody, "app_session_status", "status", "type", "state") ??
            FindStringInNamedNode(responseBody, "appSessionStatus", "status", "type", "state") ??
            FindStringByKeyInPayload(responseBody, "app_session_status", "appSessionStatus") ??
            (appSessionId is not null ? "AppSessionStatusActive" : null);

        var appEncKey =
            FindStringInTypedAction(responseBody, "app_enc", "key") ??
            FindStringByKeyInPayload(responseBody, "app_enc_key", "appEncKey");

        var appEncNonce =
            FindStringInTypedAction(responseBody, "app_enc", "nonce") ??
            FindStringByKeyInPayload(responseBody, "app_enc_nonce", "appEncNonce");

        return new NuraAuthSessionSnapshot(
            userSessionId,
            appSessionId,
            bluetoothSessionId,
            appSessionToken,
            userSessionStatus,
            appSessionStatus,
            appEncKey,
            appEncNonce,
            responseBody);
    }

    public static IReadOnlyList<NuraAuthProfileVisualisationSlot> ExtractProfileVisualisationSlots(Dictionary<string, object?>? responseBody) {
        if (responseBody is null) {
            return [];
        }

        var payload = FindTypedActionData(responseBody, "profiles");
        if (payload is null) {
            return [];
        }

        var slots = new List<NuraAuthProfileVisualisationSlot>();
        var profileId = 0;

        foreach (var entry in EnumerateArray(payload)) {
            var entryMap = TryAsObjectMap(entry);
            if (entryMap is null) {
                profileId++;
                continue;
            }

            var visualisation = TryParseVisualisation(entryMap);
            var name =
                ConvertToStringValue(GetCaseInsensitiveValue(entryMap, "name")) ??
                ConvertToStringValue(GetCaseInsensitiveValue(entryMap, "profile_name")) ??
                ConvertToStringValue(GetCaseInsensitiveValue(entryMap, "title"));

            if (visualisation is not null || !string.IsNullOrWhiteSpace(name)) {
                slots.Add(new NuraAuthProfileVisualisationSlot(profileId, name, visualisation));
            }

            profileId++;
        }

        return slots;
    }

    private static string? FindStringInTypedAction(Dictionary<string, object?> root, string category, string field) {
        var detailMap = FindTypedActionDetailMap(root, category);
        if (detailMap is null) {
            return null;
        }

        var value = ConvertToStringValue(GetCaseInsensitiveValue(detailMap, field));
        if (!string.IsNullOrWhiteSpace(value)) {
            return value;
        }

        return null;
    }

    private static object? FindTypedActionData(Dictionary<string, object?> root, string category) {
        return GetCaseInsensitiveValue(FindTypedActionDetailMap(root, category) ?? [], "_raw");
    }

    private static Dictionary<string, object?>? FindTypedActionDetailMap(Dictionary<string, object?> root, string category) {
        var dataMap = TryAsObjectMap(GetCaseInsensitiveValue(root, "d"));
        var actionsNode = dataMap is not null ? GetCaseInsensitiveValue(dataMap, "a") : null;
        if (actionsNode is null) {
            return null;
        }

        foreach (var action in EnumerateArray(actionsNode)) {
            var actionMap = TryAsObjectMap(action);
            if (actionMap is null) {
                continue;
            }

            var type = ConvertToStringValue(GetCaseInsensitiveValue(actionMap, "t"));
            var actionCategory = ConvertToStringValue(GetCaseInsensitiveValue(actionMap, "c"));
            if (!string.Equals(type, "t", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(actionCategory, category, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var detailNode = GetCaseInsensitiveValue(actionMap, "d");
            if (TryAsObjectMap(detailNode) is { } detailMap) {
                return new Dictionary<string, object?>(detailMap, StringComparer.OrdinalIgnoreCase) {
                    ["_raw"] = detailNode
                };
            }

            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) {
                ["_raw"] = detailNode
            };
        }

        return null;
    }

    private static int? FindIntInNamedNode(Dictionary<string, object?> root, string nodeName, params string[] fieldNames) {
        var nested = FindNamedNode(root, nodeName);
        if (nested is null) {
            return null;
        }

        return FindIntByKey(nested, fieldNames);
    }

    private static string? FindStringInNamedNode(Dictionary<string, object?> root, string nodeName, params string[] fieldNames) {
        var nested = FindNamedNode(root, nodeName);
        if (nested is null) {
            return null;
        }

        return FindStringByKey(nested, fieldNames);
    }

    private static Dictionary<string, object?>? FindNamedNode(Dictionary<string, object?> root, string nodeName) {
        if (TryGetMapValue(root, nodeName, out var direct) && TryAsObjectMap(direct) is { } directMap) {
            return directMap;
        }

        var dataMap = TryAsObjectMap(GetCaseInsensitiveValue(root, "d"));
        if (dataMap is null) {
            return null;
        }

        var actionsNode = GetCaseInsensitiveValue(dataMap, "a");
        if (actionsNode is null) {
            return null;
        }

        foreach (var action in EnumerateArray(actionsNode)) {
            var actionMap = TryAsObjectMap(action);
            if (actionMap is null) {
                continue;
            }

            var type = ConvertToStringValue(GetCaseInsensitiveValue(actionMap, "t"));
            var category = ConvertToStringValue(GetCaseInsensitiveValue(actionMap, "c"));
            if (!string.Equals(type, "t", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(category, nodeName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var detailMap = TryAsObjectMap(GetCaseInsensitiveValue(actionMap, "d"));
            if (detailMap is not null) {
                return detailMap;
            }
        }

        return null;
    }

    private static IEnumerable<Dictionary<string, object?>> EnumeratePayloadMaps(Dictionary<string, object?> root) {
        yield return root;

        if (TryAsObjectMap(GetCaseInsensitiveValue(root, "d")) is { } dataMap) {
            yield return dataMap;
        }
    }

    private static int? FindIntByKeyInPayload(Dictionary<string, object?> root, params string[] keys) {
        foreach (var map in EnumeratePayloadMaps(root)) {
            var value = FindIntByKey(map, keys);
            if (value is not null) {
                return value;
            }
        }

        return null;
    }

    private static string? FindStringByKeyInPayload(Dictionary<string, object?> root, params string[] keys) {
        foreach (var map in EnumeratePayloadMaps(root)) {
            var value = FindStringByKey(map, keys);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
        }

        return null;
    }

    private static int? FindIntByKey(Dictionary<string, object?> root, params string[] keys) {
        foreach (var key in keys) {
            if (!TryGetMapValue(root, key, out var value)) {
                continue;
            }

            var converted = ConvertToNullableInt32(value);
            if (converted is not null) {
                return converted;
            }
        }

        return null;
    }

    private static string? FindStringByKey(Dictionary<string, object?> root, params string[] keys) {
        foreach (var key in keys) {
            if (!TryGetMapValue(root, key, out var value)) {
                continue;
            }

            var converted = ConvertToStringValue(value);
            if (!string.IsNullOrWhiteSpace(converted)) {
                return converted;
            }
        }

        return null;
    }

    private static bool TryGetMapValue(Dictionary<string, object?> map, string key, out object? value) {
        foreach (var entry in map) {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? GetCaseInsensitiveValue(Dictionary<string, object?> map, string key) {
        return TryGetMapValue(map, key, out var value) ? value : null;
    }

    private static Dictionary<string, object?>? TryAsObjectMap(object? value) {
        return value switch {
            Dictionary<string, object?> direct => direct,
            JsonElement { ValueKind: JsonValueKind.Object } jsonObject => JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonObject.GetRawText()),
            _ => null
        };
    }

    private static IEnumerable<object?> EnumerateArray(object? value) {
        return value switch {
            List<object?> list => list,
            object?[] array => array,
            JsonElement { ValueKind: JsonValueKind.Array } jsonArray => JsonSerializer.Deserialize<List<object?>>(jsonArray.GetRawText()) ?? [],
            IEnumerable<object?> enumerable => enumerable,
            _ => []
        };
    }

    private static int? ConvertToNullableInt32(object? value) {
        return value switch {
            null => null,
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            uint ui when ui <= int.MaxValue => (int)ui,
            ulong ul when ul <= int.MaxValue => (int)ul,
            short s => s,
            ushort us => us,
            byte b => b,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ConvertToStringValue(object? value) {
        return value switch {
            null => null,
            string text => text,
            byte[] bytes => Convert.ToBase64String(bytes),
            JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
            _ => value.ToString()
        };
    }

    private static double? ConvertToNullableDouble(object? value) {
        return value switch {
            null => null,
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int intValue => intValue,
            long longValue => longValue,
            JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetDouble(out var result) => result,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when double.TryParse(jsonString.GetString(), out var result) => result,
            string text when double.TryParse(text, out var result) => result,
            _ => null
        };
    }

    private static IReadOnlyList<double>? TryGetDoubleList(Dictionary<string, object?> map, string key) {
        var node = GetCaseInsensitiveValue(map, key);
        if (node is null) {
            return null;
        }

        var values = new List<double>();
        foreach (var item in EnumerateArray(node)) {
            if (ConvertToNullableDouble(item) is not { } value) {
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    private static NuraLib.Devices.NuraProfileVisualisationData? TryParseVisualisation(Dictionary<string, object?> map) {
        var left = TryGetDoubleList(map, "left");
        var right = TryGetDoubleList(map, "right");
        if (left is null || right is null || left.Count == 0 || right.Count == 0) {
            return null;
        }

        return new NuraLib.Devices.NuraProfileVisualisationData {
            Valid = ConvertToNullableBool(GetCaseInsensitiveValue(map, "valid")) ?? true,
            Colour = ConvertToNullableDouble(GetCaseInsensitiveValue(map, "colour")) ?? 0.0,
            LeftData = left,
            RightData = right
        };
    }

    private static bool? ConvertToNullableBool(object? value) {
        return value switch {
            null => null,
            bool boolValue => boolValue,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when bool.TryParse(jsonString.GetString(), out var result) => result,
            string text when bool.TryParse(text, out var result) => result,
            _ => null
        };
    }
}

internal sealed record class NuraAuthSessionSnapshot(
    int? UserSessionId,
    int? AppSessionId,
    int? BluetoothSessionId,
    string? AppSessionToken,
    string? UserSessionStatus,
    string? AppSessionStatus,
    string? AppEncKey,
    string? AppEncNonce,
    Dictionary<string, object?>? ResponseBody) {
    public static NuraAuthSessionSnapshot Empty { get; } =
        new(null, null, null, null, null, null, null, null, null);
}

internal sealed record class NuraAuthProfileVisualisationSlot(
    int ProfileId,
    string? Name,
    NuraLib.Devices.NuraProfileVisualisationData? Visualisation);
