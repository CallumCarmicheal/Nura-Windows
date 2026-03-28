using System.Text.Json;

using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura;

internal static class AuthStateSupport {
    internal static long? ParseOptionalInt64(string[] args, string name) {
        var raw = ArgumentReader.OptionalValue(args, name);
        if (string.IsNullOrWhiteSpace(raw)) {
            return null;
        }

        if (!long.TryParse(raw, out var value)) {
            throw new InvalidOperationException($"{name} must be a valid integer");
        }

        return value;
    }

    internal static int ParseRequiredInt32(string[] args, string name) {
        var raw = ArgumentReader.RequiredValue(args, name);
        if (!int.TryParse(raw, out var value)) {
            throw new InvalidOperationException($"{name} must be a valid integer");
        }

        return value;
    }

    internal static int? ParseOptionalInt32(string[] args, string name) {
        var raw = ArgumentReader.OptionalValue(args, name);
        if (string.IsNullOrWhiteSpace(raw)) {
            return null;
        }

        if (!int.TryParse(raw, out var value)) {
            throw new InvalidOperationException($"{name} must be a valid integer");
        }

        return value;
    }

    internal static NuraAuthState ApplyAuthResultToState(
        NuraAuthState currentState,
        AuthCallResult result,
        string? emailAddress,
        string? fallbackAuthUid = null) {
        var sessionState = ExtractSessionState(result.DecodedBody);
        var responseBodyToStore = SelectResponseBodyToStore(currentState.LastResponseBody, result.DecodedBody);
        return currentState.WithAuthHeaders(
            result.AccessToken,
            result.ClientKey,
            result.AuthUid ?? fallbackAuthUid,
            result.ExpiryUnixSeconds,
            responseBodyToStore,
            emailAddress,
            userSessionId: sessionState.UserSessionId,
            appSessionId: sessionState.AppSessionId,
            bluetoothSessionId: sessionState.BluetoothSessionId,
            appSessionToken: sessionState.AppSessionToken,
            userSessionStatus: sessionState.UserSessionStatus,
            appSessionStatus: sessionState.AppSessionStatus,
            appEncKey: sessionState.AppEncKey,
            appEncNonce: sessionState.AppEncNonce);
    }

    internal static void LogSessionState(NuraAuthState state, SessionLogger logger) {
        if (state.UserSessionId is { } userSessionId) {
            logger.WriteLine($"auth.user_session_id={userSessionId}");
        }

        if (!string.IsNullOrWhiteSpace(state.UserSessionStatus)) {
            logger.WriteLine($"auth.user_session_status={state.UserSessionStatus}");
        }

        if (state.AppSessionId is { } appSessionId) {
            logger.WriteLine($"auth.app_session_id={appSessionId}");
        }

        if (state.BluetoothSessionId is { } bluetoothSessionId) {
            logger.WriteLine($"auth.bluetooth_session_id={bluetoothSessionId}");
        }

        if (!string.IsNullOrWhiteSpace(state.AppSessionToken)) {
            logger.WriteLine($"auth.app_session_token={state.AppSessionToken}");
        }

        if (!string.IsNullOrWhiteSpace(state.AppSessionStatus)) {
            logger.WriteLine($"auth.app_session_status={state.AppSessionStatus}");
        }

        if (!string.IsNullOrWhiteSpace(state.AppEncKey)) {
            logger.WriteLine($"auth.app_enc.key={state.AppEncKey}");
        }

        if (!string.IsNullOrWhiteSpace(state.AppEncNonce)) {
            logger.WriteLine($"auth.app_enc.nonce={state.AppEncNonce}");
        }
    }

    internal static string SummarizeSessionStartResponse(Dictionary<string, object?> responseBody) {
        var details = SessionStartResponseParser.Parse(responseBody);
        if (details is null) {
            return "no_actions";
        }

        var parts = new List<string> {
            $"packets={details.Packets.Count}",
            $"run_packets={details.RunPackets.Count}"
        };

        if (details.NumberValue is { } numberValue) {
            parts.Add($"number={numberValue}");
        }

        if (details.SessionId is { } sessionId) {
            parts.Add($"session={sessionId}");
        }

        if (!string.IsNullOrWhiteSpace(details.FinalEvent)) {
            parts.Add($"final={details.FinalEvent}");
        }

        return string.Join(" ", parts);
    }

    private static Dictionary<string, object?>? SelectResponseBodyToStore(
        Dictionary<string, object?>? currentBody,
        Dictionary<string, object?>? nextBody) {
        var currentIsContinuation = currentBody is not null && SessionStartResponseParser.Parse(currentBody) is not null;
        var nextIsContinuation = nextBody is not null && SessionStartResponseParser.Parse(nextBody) is not null;

        if (nextIsContinuation) {
            return nextBody;
        }

        if (currentIsContinuation) {
            return currentBody;
        }

        return nextBody ?? currentBody;
    }

    private static SessionStateSnapshot ExtractSessionState(Dictionary<string, object?>? responseBody) {
        if (responseBody is null) {
            return SessionStateSnapshot.Empty;
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
            FindIntByKey(responseBody, "bsid") ??
            SessionStartResponseParser.Parse(responseBody)?.SessionId;

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

        return new SessionStateSnapshot(
            userSessionId,
            appSessionId,
            bluetoothSessionId,
            appSessionToken,
            userSessionStatus,
            appSessionStatus,
            appEncKey,
            appEncNonce);
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

    private static Dictionary<string, object?>? TryAsObjectMap(object? node) {
        return node switch {
            Dictionary<string, object?> map => map,
            JsonElement { ValueKind: JsonValueKind.Object } jsonObject => JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonObject.GetRawText()),
            _ => null
        };
    }

    private static IEnumerable<object?> EnumerateArray(object? node) {
        return node switch {
            List<object?> list => list,
            JsonElement { ValueKind: JsonValueKind.Array } jsonArray => JsonSerializer.Deserialize<List<object?>>(jsonArray.GetRawText()) ?? [],
            _ => []
        };
    }

    private static object? GetCaseInsensitiveValue(Dictionary<string, object?> map, string key) {
        foreach (var entry in map) {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) {
                return entry.Value;
            }
        }

        return null;
    }

    private static bool TryFindValueByKey(object? node, string key, out object? value) {
        switch (node) {
            case Dictionary<string, object?> map:
                foreach (var entry in map) {
                    if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) {
                        value = entry.Value;
                        return true;
                    }

                    if (TryFindValueByKey(entry.Value, key, out value)) {
                        return true;
                    }
                }
                break;
            case List<object?> list:
                foreach (var item in list) {
                    if (TryFindValueByKey(item, key, out value)) {
                        return true;
                    }
                }
                break;
            case JsonElement jsonElement:
                if (jsonElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array) {
                    var converted = JsonSerializer.Deserialize<object?>(jsonElement.GetRawText());
                    if (TryFindValueByKey(converted, key, out value)) {
                        return true;
                    }
                }
                break;
        }

        value = null;
        return false;
    }

    private static int? FindIntInNamedNode(Dictionary<string, object?> root, string nodeName, params string[] candidateKeys) {
        if (!TryFindValueByKey(root, nodeName, out var node)) {
            return null;
        }

        return FindIntInNode(node, candidateKeys);
    }

    private static string? FindStringInNamedNode(Dictionary<string, object?> root, string nodeName, params string[] candidateKeys) {
        if (!TryFindValueByKey(root, nodeName, out var node)) {
            return null;
        }

        return FindStringInNode(node, candidateKeys);
    }

    private static int? FindIntByKey(Dictionary<string, object?> root, string key) {
        return TryFindValueByKey(root, key, out var node) ? ConvertToInt32(node) : null;
    }

    private static string? FindStringByKey(Dictionary<string, object?> root, string key) {
        return TryFindValueByKey(root, key, out var node) ? ConvertToStringValue(node) : null;
    }

    private static int? FindIntInNode(object? node, params string[] candidateKeys) {
        foreach (var candidateKey in candidateKeys) {
            if (TryFindValueByKey(node, candidateKey, out var value)) {
                var parsed = ConvertToInt32(value);
                if (parsed is not null) {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static string? FindStringInNode(object? node, params string[] candidateKeys) {
        foreach (var candidateKey in candidateKeys) {
            if (TryFindValueByKey(node, candidateKey, out var value)) {
                var parsed = ConvertToStringValue(value);
                if (!string.IsNullOrWhiteSpace(parsed)) {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static int? ConvertToInt32(object? node) {
        return node switch {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetInt32(out var intValue) => intValue,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when int.TryParse(jsonString.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ConvertToStringValue(object? node) {
        return node switch {
            string stringValue => stringValue,
            byte[] bytes when bytes.Length > 0 => Convert.ToBase64String(bytes),
            JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
            _ => null
        };
    }

    private readonly record struct SessionStateSnapshot(
        int? UserSessionId,
        int? AppSessionId,
        int? BluetoothSessionId,
        string? AppSessionToken,
        string? UserSessionStatus,
        string? AppSessionStatus,
        string? AppEncKey,
        string? AppEncNonce) {
        internal static SessionStateSnapshot Empty => new(null, null, null, null, null, null, null, null);
    }
}
