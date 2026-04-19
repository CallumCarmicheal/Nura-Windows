using System.Text.Encodings.Web;
using System.Text.Json;

namespace NuraLib.Configuration;

/// <summary>
/// Loads and saves <see cref="NuraConfig"/> instances from JSON files.
/// </summary>
public static class NuraConfigStore {
    /// <summary>
    /// Loads a configuration file from disk.
    /// </summary>
    /// <param name="path">The full path to the configuration file.</param>
    /// <returns>The parsed configuration instance.</returns>
    public static NuraConfig Load(string path) {
        if (!File.Exists(path)) {
            throw new FileNotFoundException($"config not found: {path}");
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NuraConfig>(json, JsonOptions())
               ?? throw new InvalidOperationException("failed to parse config json");
    }

    /// <summary>
    /// Loads an existing configuration file or creates and saves a new one when the file does not exist.
    /// </summary>
    /// <param name="path">The full path to the configuration file.</param>
    /// <param name="factory">Optional factory used to create the initial configuration.</param>
    /// <returns>The loaded or newly created configuration instance.</returns>
    public static NuraConfig LoadOrCreate(string path, Func<NuraConfig>? factory = null) {
        if (!File.Exists(path)) {
            var config = factory?.Invoke() ?? new NuraConfig();
            Save(path, config);
            return config;
        }

        return Load(path);
    }

    /// <summary>
    /// Saves a configuration file to disk.
    /// </summary>
    /// <param name="path">The full path to the configuration file.</param>
    /// <param name="config">The configuration instance to persist.</param>
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
