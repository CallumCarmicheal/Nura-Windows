using System.IO;
using System.Text.Json;

using NuraPopupWpf.Models;

namespace NuraPopupWpf.Services;

public sealed class AppSettingStore {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly string[] _legacySettingsPaths;

    public AppSettingStore(string settingsPath, params string[] legacySettingsPaths) {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        var settingsDirectory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("App settings path must include a directory.");
        Directory.CreateDirectory(settingsDirectory);
        _settingsPath = settingsPath;
        _legacySettingsPaths = legacySettingsPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
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

        return _legacySettingsPaths.FirstOrDefault(File.Exists);
    }

    private static AppSettings DeserializeSettings(string json) {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();

        if (settings.Preferences is null) {
            settings.Preferences = new AppPreferences();
        }

        return MigrateLegacyFlatSettings(json, settings);
    }

    private static AppSettings MigrateLegacyFlatSettings(string json, AppSettings settings) {
        try {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(nameof(AppSettings.Preferences), out _)) {
                return settings;
            }

            if (document.RootElement.TryGetProperty(nameof(AppSettings.AutoSetupDevices), out var autoSetupElement) &&
                autoSetupElement.ValueKind is JsonValueKind.True or JsonValueKind.False) {
                settings.AutoSetupDevices = autoSetupElement.GetBoolean();
            }

            if (document.RootElement.TryGetProperty(nameof(AppPreferences.AnchorEdge), out var anchorEdgeElement)) {
                settings.Preferences.AnchorEdge = ParseAnchorEdge(anchorEdgeElement) ?? settings.Preferences.AnchorEdge;
            }

            if (document.RootElement.TryGetProperty(nameof(AppPreferences.RememberExpandType), out var rememberExpandElement)) {
                settings.Preferences.RememberExpandType = ParseRememberExpandType(rememberExpandElement) ?? settings.Preferences.RememberExpandType;
            }

            if (document.RootElement.TryGetProperty(nameof(AppPreferences.LastLeft), out var lastLeftElement) &&
                lastLeftElement.TryGetDouble(out var lastLeft)) {
                settings.Preferences.LastLeft = lastLeft;
            }

            if (document.RootElement.TryGetProperty(nameof(AppPreferences.LastTop), out var lastTopElement) &&
                lastTopElement.TryGetDouble(out var lastTop)) {
                settings.Preferences.LastTop = lastTop;
            }

            if (!document.RootElement.TryGetProperty(nameof(AppPreferences.AnchorMode), out var anchorModeElement)) {
                return settings;
            }

            if (ParseAnchorMode(anchorModeElement) is { } anchorMode) {
                settings.Preferences.AnchorMode = anchorMode;
            }

            return settings;
        } catch {
            return settings;
        }
    }

    private static WindowAnchorMode? ParseAnchorMode(JsonElement element) {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numericMode)) {
            return numericMode switch {
                0 => WindowAnchorMode.Taskbar,
                1 => WindowAnchorMode.RememberLastPosition,
                2 => WindowAnchorMode.AnchorEdge,
                _ => null
            };
        }

        if (element.ValueKind != JsonValueKind.String) {
            return null;
        }

        return element.GetString() switch {
            "Taskbar" => WindowAnchorMode.Taskbar,
            "RememberLastPosition" => WindowAnchorMode.RememberLastPosition,
            "Center" => WindowAnchorMode.AnchorEdge,
            "AnchorEdge" => WindowAnchorMode.AnchorEdge,
            _ => null
        };
    }

    private static WindowAnchorEdge? ParseAnchorEdge(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Number when element.TryGetInt32(out var numericEdge) && Enum.IsDefined(typeof(WindowAnchorEdge), numericEdge) =>
                (WindowAnchorEdge)numericEdge,
            JsonValueKind.String when Enum.TryParse<WindowAnchorEdge>(element.GetString(), out var edge) =>
                edge,
            _ => null
        };
    }

    private static RememberExpandType? ParseRememberExpandType(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Number when element.TryGetInt32(out var numericValue) && Enum.IsDefined(typeof(RememberExpandType), numericValue) =>
                (RememberExpandType)numericValue,
            JsonValueKind.String when Enum.TryParse<RememberExpandType>(element.GetString(), out var expandType) =>
                expandType,
            _ => null
        };
    }
}
