using System.IO;
using System.Text.Json;

using NuraPopupWpf.Models;

namespace NuraPopupWpf.Services;

public sealed class AppSettingStore {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingStore(string settingsPath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var settingsDirectory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("App settings path must include a directory.");

        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = settingsPath;
    }

    public AppSettings Load() {
        try {
            var path = ResolveLoadPath();
            if (path is null) {
                return new AppSettings();
            }

            var json = File.ReadAllText(path);
            var settings = DeserializeSettings(json);

            if (!string.Equals(path, _settingsPath, StringComparison.OrdinalIgnoreCase)) {
                Save(settings);
            }

            return settings;
        } catch {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private string? ResolveLoadPath() {
        if (File.Exists(_settingsPath)) {
            return _settingsPath;
        }

        return null;
    }

    private static AppSettings DeserializeSettings(string json) {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();

        if (settings.Preferences is null) {
            settings.Preferences = new AppPreferences();
        }

        return settings;
    }
}
