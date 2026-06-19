using System.IO;

namespace NuraPopupWpf.Bootstrap;

public sealed record class PopupAppStoragePaths(
    string RootDirectory,
    string AppSettingsPath,
    string LegacyAppSettingsPath,
    string LegacyWindowPreferencesPath,
    string NuraConfigPath,
    string LegacyNuraConfigPath
) {
    public static PopupAppStoragePaths Create(PopupAppBootstrapMode mode) {
        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NuraApp");
        var rootDirectory = mode == PopupAppBootstrapMode.Demo
            ? Path.Combine(appDirectory, "demo")
            : appDirectory;

        Directory.CreateDirectory(rootDirectory);

        return new PopupAppStoragePaths(
            rootDirectory,
            Path.Combine(rootDirectory, "app-settings.json"),
            Path.Combine(rootDirectory, "ui-settings.json"),
            Path.Combine(rootDirectory, "window-preferences.json"),
            Path.Combine(appDirectory, "nura-config.json"),
            Path.Combine(appDirectory, "nura-settings.json"));
    }
}
