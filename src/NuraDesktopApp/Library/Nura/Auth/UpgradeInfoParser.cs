using System.Text.Json;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class UpgradeInfoParser {
    internal static UpgradeInfoSnapshot Parse(Dictionary<string, object?> responseBody) {
        if (!TryAsMap(GetMapValue(responseBody, "d"), out var dataMap)) {
            return UpgradeInfoSnapshot.Empty;
        }

        return new UpgradeInfoSnapshot(
            Classic: ParseClassicUpgrade(dataMap, "upgrade"),
            Tws: ParseTwsUpgrade(dataMap, "tws_upgrade"),
            CurrentTws: ParseTwsUpgrade(dataMap, "current_tws_upgrade"));
    }

    private static UpgradeMetadata? ParseClassicUpgrade(Dictionary<string, object?> dataMap, string category) {
        foreach (var action in EnumerateActionMaps(dataMap)) {
            if (!string.Equals(GetString(GetMapValue(action, "t")), "t", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!string.Equals(GetString(GetMapValue(action, "c")), category, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!TryAsMap(GetMapValue(action, "d"), out var detailMap)) {
                continue;
            }

            return new UpgradeMetadata(
                Name: GetString(GetMapValue(detailMap, "name")),
                Description: GetString(GetMapValue(detailMap, "description")),
                TargetFirmwareVersion: GetInt(GetMapValue(detailMap, "target_fw")),
                TargetPersistentStoreVersion: GetInt(GetMapValue(detailMap, "target_ps")),
                TargetFilesystemVersion: GetInt(GetMapValue(detailMap, "target_fs")),
                Blocking: GetBool(GetMapValue(detailMap, "blocking")),
                CreatedAtUnixSeconds: GetLong(GetMapValue(detailMap, "created_at")),
                InfoUrl: GetString(GetMapValue(detailMap, "info_url")),
                DesignJson: SerializeJson(GetMapValue(detailMap, "design")),
                Languages: ExtractStringArray(GetMapValue(detailMap, "languages")),
                Raw: detailMap);
        }

        return null;
    }

    private static TwsUpgradeMetadata? ParseTwsUpgrade(Dictionary<string, object?> dataMap, string category) {
        foreach (var action in EnumerateActionMaps(dataMap)) {
            if (!string.Equals(GetString(GetMapValue(action, "t")), "t", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!string.Equals(GetString(GetMapValue(action, "c")), category, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!TryAsMap(GetMapValue(action, "d"), out var detailMap)) {
                continue;
            }

            var files = ExtractTwsFiles(detailMap);
            return new TwsUpgradeMetadata(
                Name: GetString(GetMapValue(detailMap, "name")),
                Description: GetString(GetMapValue(detailMap, "description")),
                TargetFirmwareVersion: GetInt(GetMapValue(detailMap, "target_fw")),
                TargetPersistentStoreVersion: GetInt(GetMapValue(detailMap, "target_ps")),
                TargetFilesystemVersion: GetInt(GetMapValue(detailMap, "target_fs")),
                Blocking: GetBool(GetMapValue(detailMap, "blocking")),
                CreatedAtUnixSeconds: GetLong(GetMapValue(detailMap, "created_at")),
                InfoUrl: GetString(GetMapValue(detailMap, "info_url")),
                DesignJson: SerializeJson(GetMapValue(detailMap, "design")),
                Languages: ExtractStringArray(GetMapValue(detailMap, "languages")),
                Files: files,
                Raw: detailMap);
        }

        return null;
    }

    private static IReadOnlyDictionary<string, TwsUpgradeFile> ExtractTwsFiles(Dictionary<string, object?> detailMap) {
        foreach (var preferredKey in new[] { "files", "upgrade_files", "language_files" }) {
            if (TryAsMap(GetMapValue(detailMap, preferredKey), out var candidateFiles)) {
                var parsed = ParseTwsFileMap(candidateFiles);
                if (parsed.Count > 0) {
                    return parsed;
                }
            }
        }

        foreach (var entry in detailMap) {
            if (!TryAsMap(entry.Value, out var candidateFiles)) {
                continue;
            }

            var parsed = ParseTwsFileMap(candidateFiles);
            if (parsed.Count > 0) {
                return parsed;
            }
        }

        return new Dictionary<string, TwsUpgradeFile>(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, TwsUpgradeFile> ParseTwsFileMap(Dictionary<string, object?> map) {
        var result = new Dictionary<string, TwsUpgradeFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in map) {
            if (!TryAsMap(entry.Value, out var fileMap)) {
                continue;
            }

            var url = GetString(GetMapValue(fileMap, "url"));
            var md5 = GetString(GetMapValue(fileMap, "md5"));
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(md5)) {
                continue;
            }

            result[entry.Key] = new TwsUpgradeFile(url, md5);
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractStringArray(object? node) {
        var result = new List<string>();
        foreach (var item in EnumerateArray(node)) {
            var value = GetString(item);
            if (!string.IsNullOrWhiteSpace(value)) {
                result.Add(value);
            }
        }

        return result;
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

    private static string? GetString(object? node) {
        return node switch {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
            _ => null
        };
    }

    private static bool? GetBool(object? node) {
        return node switch {
            bool value => value,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when bool.TryParse(jsonString.GetString(), out var value) => value,
            _ => null
        };
    }

    private static int? GetInt(object? node) {
        return node switch {
            int value => value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetInt32(out var value) => value,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when int.TryParse(jsonString.GetString(), out var value) => value,
            _ => null
        };
    }

    private static long? GetLong(object? node) {
        return node switch {
            long value => value,
            int value => value,
            JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetInt64(out var value) => value,
            JsonElement { ValueKind: JsonValueKind.String } jsonString when long.TryParse(jsonString.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? SerializeJson(object? node) {
        return node is null ? null : JsonSerializer.Serialize(node);
    }
}

internal sealed record UpgradeInfoSnapshot(
    UpgradeMetadata? Classic,
    TwsUpgradeMetadata? Tws,
    TwsUpgradeMetadata? CurrentTws) {
    internal static readonly UpgradeInfoSnapshot Empty = new(null, null, null);

    internal bool HasAnyData =>
        Classic is not null ||
        Tws is not null ||
        CurrentTws is not null;
}

internal record UpgradeMetadata(
    string? Name,
    string? Description,
    int? TargetFirmwareVersion,
    int? TargetPersistentStoreVersion,
    int? TargetFilesystemVersion,
    bool? Blocking,
    long? CreatedAtUnixSeconds,
    string? InfoUrl,
    string? DesignJson,
    IReadOnlyList<string> Languages,
    IReadOnlyDictionary<string, object?> Raw);

internal sealed record TwsUpgradeMetadata(
    string? Name,
    string? Description,
    int? TargetFirmwareVersion,
    int? TargetPersistentStoreVersion,
    int? TargetFilesystemVersion,
    bool? Blocking,
    long? CreatedAtUnixSeconds,
    string? InfoUrl,
    string? DesignJson,
    IReadOnlyList<string> Languages,
    IReadOnlyDictionary<string, TwsUpgradeFile> Files,
    IReadOnlyDictionary<string, object?> Raw)
    : UpgradeMetadata(Name, Description, TargetFirmwareVersion, TargetPersistentStoreVersion, TargetFilesystemVersion, Blocking, CreatedAtUnixSeconds, InfoUrl, DesignJson, Languages, Raw);

internal sealed record TwsUpgradeFile(
    string Url,
    string Md5);
