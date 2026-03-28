using System.Text.Encodings.Web;
using System.Text.Json;

namespace NuraLib.Configuration;

public static class NuraConfigStore {
    public static NuraConfig Load(string path) {
        if (!File.Exists(path)) {
            throw new FileNotFoundException($"config not found: {path}");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NuraConfig>(json, JsonOptions())
               ?? throw new InvalidOperationException("failed to parse config json");
    }

    public static NuraConfig LoadOrCreate(string path, Func<NuraConfig>? factory = null) {
        if (!File.Exists(path)) {
            var config = factory?.Invoke() ?? new NuraConfig();
            Save(path, config);
            return config;
        }

        return Load(path);
    }

    public static void Save(string path, NuraConfig config) {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(config, JsonOptions());
        File.WriteAllText(path, json);
    }

    private static JsonSerializerOptions JsonOptions() {
        return new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }
}
