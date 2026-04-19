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
            FindIntByKey(responseBody, "user_session_id") ??
            FindIntByKey(responseBody, "userSessionId") ??
            FindIntByKey(responseBody, "sessionId") ??
            FindIntByKey(responseBody, "usid");

        var appSessionId =
            FindIntInNamedNode(responseBody, "app_session_status", "id", "app_session_id", "appSessionId", "session_id", "sessionId") ??
            FindIntInNamedNode(responseBody, "appSessionStatus", "id", "app_session_id", "appSessionId", "session_id", "sessionId") ??
            FindIntInNamedNode(responseBody, "app_session", "id", "app_session_id", "appSessionId", "session_id", "sessionId") ??
            FindIntInNamedNode(responseBody, "appSession", "id", "app_session_id", "appSessionId", "session_id", "sessionId") ??
            FindIntByKey(responseBody, "app_session_id") ??
            FindIntByKey(responseBody, "appSessionId") ??
            FindIntByKey(responseBody, "asid");

        var bluetoothSessionId =
            FindIntInNamedNode(responseBody, "bluetooth_session_status", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntInNamedNode(responseBody, "bluetoothSessionStatus", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntInNamedNode(responseBody, "bluetooth_session", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntInNamedNode(responseBody, "bluetoothSession", "id", "bluetooth_session_id", "bluetoothSessionId", "session_id", "sessionId", "bsid") ??
            FindIntByKey(responseBody, "bluetooth_session_id") ??
            FindIntByKey(responseBody, "bluetoothSessionId") ??
            FindIntByKey(responseBody, "bsid");

        var appSessionToken =
            FindStringInNamedNode(responseBody, "app_session_status", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringInNamedNode(responseBody, "appSessionStatus", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringInNamedNode(responseBody, "app_session", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringInNamedNode(responseBody, "appSession", "a", "app_session_token", "appSessionToken", "token") ??
            FindStringByKey(responseBody, "app_session_token") ??
            FindStringByKey(responseBody, "appSessionToken") ??
            FindStringByKey(responseBody, "a");

        var userSessionStatus =
            FindStringInNamedNode(responseBody, "user_session_status", "status", "type", "state") ??
            FindStringInNamedNode(responseBody, "userSessionStatus", "status", "type", "state") ??
            FindStringByKey(responseBody, "user_session_status") ??
            FindStringByKey(responseBody, "userSessionStatus");

        var appSessionStatus =
            FindStringInNamedNode(responseBody, "app_session_status", "status", "type", "state") ??
            FindStringInNamedNode(responseBody, "appSessionStatus", "status", "type", "state") ??
            FindStringByKey(responseBody, "app_session_status") ??
            FindStringByKey(responseBody, "appSessionStatus") ??
            (appSessionId is not null ? "AppSessionStatusActive" : null);

        var appEncKey =
            FindStringInTypedAction(responseBody, "app_enc", "key") ??
            FindStringByKey(responseBody, "app_enc_key") ??
            FindStringByKey(responseBody, "appEncKey");

        var appEncNonce =
            FindStringInTypedAction(responseBody, "app_enc", "nonce") ??
            FindStringByKey(responseBody, "app_enc_nonce") ??
            FindStringByKey(responseBody, "appEncNonce");

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

    private static string? FindStringInTypedAction(Dictionary<string, object?> root, string category, string field) {
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

            var detailMap = TryAsObjectMap(GetCaseInsensitiveValue(actionMap, "d"));
            if (detailMap is null) {
                continue;
            }

            var value = ConvertToStringValue(GetCaseInsensitiveValue(detailMap, field));
            if (!string.IsNullOrWhiteSpace(value)) {
                return value;
            }
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
        return value as Dictionary<string, object?>;
    }

    private static IEnumerable<object?> EnumerateArray(object? value) {
        return value switch {
            List<object?> list => list,
            object?[] array => array,
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
            _ => value.ToString()
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
