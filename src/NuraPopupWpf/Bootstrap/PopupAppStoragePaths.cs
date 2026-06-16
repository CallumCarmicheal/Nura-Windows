using System.IO;

namespace NuraPopupWpf.Bootstrap;

public sealed record class PopupAppStoragePaths(
    string RootDirectory,
    string WindowPreferencesPath,
    string? NuraConfigPath
) {
    public static PopupAppStoragePaths Create(PopupAppBootstrapMode mode) {
        var profileDirectoryName = mode == PopupAppBootstrapMode.Demo ? "demo" : "";
        var rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NuraApp",
            profileDirectoryName);

        Directory.CreateDirectory(rootDirectory);

        return new PopupAppStoragePaths(
            rootDirectory,
            Path.Combine(rootDirectory, "window-preferences.json"),
            mode == PopupAppBootstrapMode.Live
                ? Path.Combine(rootDirectory, "nura-settings.json")
                : null);
    }
}
