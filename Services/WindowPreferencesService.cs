using System.IO;
using System.Text.Json;

using NuraPopupWpf.Models;

namespace NuraPopupWpf.Services;

public sealed class WindowPreferencesService {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true
    };

    private readonly string _preferencesPath;

    public WindowPreferencesService() {
        var preferencesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NuraPopupWpf");

        Directory.CreateDirectory(preferencesDirectory);
        _preferencesPath = Path.Combine(preferencesDirectory, "window-preferences.json");
    }

    public WindowPreferences Load() {
        try {
            if (!File.Exists(_preferencesPath)) {
                return new WindowPreferences();
            }

            var json = File.ReadAllText(_preferencesPath);
            return JsonSerializer.Deserialize<WindowPreferences>(json, SerializerOptions) ?? new WindowPreferences();
        } catch {
            return new WindowPreferences();
        }
    }

    public void Save(WindowPreferences preferences) {
        var json = JsonSerializer.Serialize(preferences, SerializerOptions);
        File.WriteAllText(_preferencesPath, json);
    }
}
