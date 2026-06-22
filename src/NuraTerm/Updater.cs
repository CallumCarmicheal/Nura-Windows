using System;
using System.Collections.Generic;
using System.Text;

using UpdateLib;

namespace NuraTerm;

internal class Updater {

    private readonly AsyncConsoleLogger logger = new();

    public Updater(AsyncConsoleLogger logger) {
        this.logger = logger;
    }

    public async Task HandleUpdatesAsync(CancellationToken cancellationToken) {
        LogInfo("Updater", "Checking for updates...");
        logger.SetHoistedSection("status", "Checking for updates...");

        UpdateCheckResult check = await AutoUpdater.IsUpdateAvailableAsync(cancellationToken);

        if (!check.IsUpdateAvailable) {
            var isSkipped = check.Reason?.Contains("currently skipped", StringComparison.OrdinalIgnoreCase) == true;
            LogInfo("Updater", (isSkipped ? "Skipping update: " : "No update found: ") + check.Reason);
            logger.SetHoistedSection("status", isSkipped ? "Latest update is skipped." : "No updates found.");
            return;
        }

        var updateToLatest = await logger.PromptYesNoAsync("A new update has been found, do you wish to update to the latest version? [Y/n] ", true);

        if (!updateToLatest) {
            var skipThisRelease = await logger.PromptYesNoAsync("Do you want to skip this version and suppress this message until a new release? [y/N] ", false);

            if (skipThisRelease) {
                AutoUpdater.SkipUpdate(check.Update!);
                logger.WriteLine($"Skipping release version and suppressing update alerts for: {check.Update!.VersionText}.");
            }

            return;
        }

        try {
            // Use to show download progress
            logger.SetHoistedSection("current device:state", "Waiting for download progress...");
            logger.SetHoistedSection("status", NuraGradient.Text("Downloading update..."));

            DownloadedUpdate downloaded = await AutoUpdater.DownloadUpdateAsync(
                check.Update!,
                progress: new Progress<UpdateDownloadProgress>(
                    p => logger.SetHoistedSection(
                        "current device:state",
                        FormatDownloadProgress(p)
                    )),
                cancellationToken
            );

            LogSuccess("Update", "Update downloaded successfully.");
            logger.SetHoistedSection("current device:state", "");
            logger.SetHoistedSection("status", NuraGradient.Text("Installing update..."));
            LogInfo("Update", "Installing update...");
                
            // Install and restart.
            AutoUpdater.InstallAndRestart(downloaded);
        } catch (Exception ex) {
            LogError("Update", $"Update failed: {ex.Message}");
            logger.SetHoistedSection("status", "Update failed.");

            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Error("[Update] "),
                AnsiPart.Dim(ex.ToString()));
        } finally {
            // Reset status line
            logger.SetHoistedSection("current device:state", "");
        }
    }

    private void LogInfo(string area, string message) {
        logger.WriteLine(
            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
            AnsiPart.Info($"[{area}] "),
            message);
    }

    private void LogSuccess(string area, string message) {
        logger.WriteLine(
            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
            AnsiPart.Success($"[{area}] "),
            $"{message}");
    }

    private void LogWarning(string area, string message) {
        logger.WriteLine(
            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
            AnsiPart.Warning($"[{area}] "),
            $"{message}");
    }

    private void LogError(string area, string message) {
        logger.WriteLine(
            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
            AnsiPart.Error($"[{area}] "),
            $"{message}");
    }

    private static string FormatDownloadProgress(UpdateDownloadProgress p) {
        double progress = p.Percent.HasValue
            ? Math.Clamp(p.Percent.Value / 100.0, 0, 1)
            : 0;

        string bar = CreateProgressBar(progress, width: 28);

        string percent = p.Percent.HasValue
            ? $"{p.Percent.Value:0.0}%"
            : "--.-%";

        string total = p.TotalBytes.HasValue
            ? FormatBytes(p.TotalBytes.Value)
            : "unknown";

        return
            $"{bar} {percent}  ·  " +
            $"{FormatBytes(p.BytesReceived)} / {total}  ·  " +
            $"{FormatBytes((long)p.BytesPerSecond)}/s  ·  " +
            $"ETA {FormatEta(p.EstimatedTimeRemaining)}";
    }

    private static string CreateProgressBar(double progress, int width) {
        progress = Math.Clamp(progress, 0, 1);

        int filled = (int)Math.Round(progress * width);
        int empty = width - filled;

        return "[" + new string('█', filled) + new string('░', empty) + "]";
    }

    private static string FormatBytes(long bytes) {
        string[] units = ["B", "KB", "MB", "GB", "TB"];

        double size = bytes;
        int unit = 0;

        while (size >= 1024 && unit < units.Length - 1) {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{size:0} {units[unit]}"
            : $"{size:0.0} {units[unit]}";
    }

    private static string FormatEta(TimeSpan? eta) {
        if (eta is null)
            return "--:--";

        if (eta.Value.TotalHours >= 1)
            return eta.Value.ToString(@"h\:mm\:ss");

        return eta.Value.ToString(@"m\:ss");
    }
}
