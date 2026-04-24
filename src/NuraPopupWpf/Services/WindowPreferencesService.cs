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
            var preferences = JsonSerializer.Deserialize<WindowPreferences>(json, SerializerOptions) ?? new WindowPreferences();
            return MigrateLegacyPreferences(json, preferences);
        } catch {
            return new WindowPreferences();
        }
    }

    public void Save(WindowPreferences preferences) {
        var json = JsonSerializer.Serialize(preferences, SerializerOptions);
        File.WriteAllText(_preferencesPath, json);
    }

    private static WindowPreferences MigrateLegacyPreferences(string json, WindowPreferences preferences) {
        try {
            using var document = JsonDocument.Parse(json);
            var isLegacyShape =
                !document.RootElement.TryGetProperty(nameof(WindowPreferences.AnchorEdge), out _) &&
                !document.RootElement.TryGetProperty(nameof(WindowPreferences.RememberExpandType), out _);

            if (!isLegacyShape) {
                return preferences;
            }

            if (!document.RootElement.TryGetProperty(nameof(WindowPreferences.AnchorMode), out var anchorModeElement)) {
                return preferences;
            }

            var oldAnchorMode = anchorModeElement.ValueKind switch {
                JsonValueKind.Number when anchorModeElement.TryGetInt32(out var numericMode) => numericMode,
                JsonValueKind.String => anchorModeElement.GetString() switch {
                    "Taskbar" => 0,
                    "RememberLastPosition" => 1,
                    "Center" => 2,
                    _ => -1
                },
                _ => -1
            };

            if (oldAnchorMode == 0) {
                preferences.AnchorMode = WindowAnchorMode.Taskbar;
            } else if (oldAnchorMode == 1) {
                preferences.AnchorMode = WindowAnchorMode.RememberLastPosition;
            } else if (oldAnchorMode == 2) {
                preferences.AnchorMode = WindowAnchorMode.AnchorEdge;
                preferences.AnchorEdge = WindowAnchorEdge.Center;
            }

            return preferences;
        } catch {
            return preferences;
        }
    }
}
