using System.IO;
using System.Reflection;

namespace NuraDesktop.Bootstrap;

public sealed record class PopupAppStoragePaths(
    string RootDirectory,
    string AppSettingsPath,
    string NuraConfigPath
) {
    public static PopupAppStoragePaths Create(PopupAppBootstrapMode mode) {
        //var appDirectory = Path.Combine(
        //    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        //    "Nura-Windows");

        var appDirectory = AppContext.BaseDirectory;

        var rootDirectory = mode == PopupAppBootstrapMode.Demo
            ? Path.Combine(appDirectory, "demo")
            : appDirectory;

        return new PopupAppStoragePaths(
            appDirectory,
            Path.Combine(rootDirectory, "settings.gui.json"),
            Path.Combine(rootDirectory, "settings.nura.json")
        );
    }
}
