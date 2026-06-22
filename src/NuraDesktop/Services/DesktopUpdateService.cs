using System.Diagnostics;

using NuraDesktop.Infrastructure;

using UpdateLib;

namespace NuraDesktop.Services;

/// <summary>
/// Owns desktop update state shared by startup UI and the Settings page.
/// </summary>
public sealed class DesktopUpdateService : ObservableObject {
    private UpdateInfo? _availableUpdate;
    private string _statusText = "Updates have not been checked yet.";
    private string _errorText = string.Empty;
    private bool _isChecking;
    private bool _isDownloading;
    private bool _isAvailableUpdateSkipped;
    private double _downloadProgressPercent;
    private string _downloadProgressText = string.Empty;

    public string AppVersionText => AutoUpdater.GetCurrentVersionText();

    public UpdateInfo? AvailableUpdate {
        get => _availableUpdate;
        private set {
            if (!SetProperty(ref _availableUpdate, value)) {
                return;
            }

            OnPropertyChanged(nameof(HasAvailableUpdate));
            OnPropertyChanged(nameof(AvailableUpdateVersionText));
            OnPropertyChanged(nameof(AvailableReleaseName));
            OnPropertyChanged(nameof(CanUpdateNow));
            OnPropertyChanged(nameof(CanViewRelease));
        }
    }

    public bool HasAvailableUpdate => AvailableUpdate is not null;

    public string AvailableUpdateVersionText => AvailableUpdate?.VersionText ?? string.Empty;

    public string AvailableReleaseName => AvailableUpdate?.ReleaseName ?? string.Empty;

    public string StatusText {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ErrorText {
        get => _errorText;
        private set {
            if (SetProperty(ref _errorText, value)) {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsChecking {
        get => _isChecking;
        private set {
            if (SetProperty(ref _isChecking, value)) {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(CanUpdateNow));
                OnPropertyChanged(nameof(CanViewRelease));
            }
        }
    }

    public bool IsDownloading {
        get => _isDownloading;
        private set {
            if (SetProperty(ref _isDownloading, value)) {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(CanCheckForUpdates));
                OnPropertyChanged(nameof(CanUpdateNow));
                OnPropertyChanged(nameof(CanViewRelease));
            }
        }
    }

    public bool IsBusy => IsChecking || IsDownloading;

    public bool CanCheckForUpdates => !IsBusy;

    public bool IsAvailableUpdateSkipped {
        get => _isAvailableUpdateSkipped;
        private set {
            if (SetProperty(ref _isAvailableUpdateSkipped, value)) {
                OnPropertyChanged(nameof(CanUpdateNow));
            }
        }
    }

    public bool CanUpdateNow => HasAvailableUpdate && !IsBusy;

    public bool CanViewRelease => !IsBusy && !string.IsNullOrWhiteSpace(AvailableUpdate?.ReleaseUrl);

    public double DownloadProgressPercent {
        get => _downloadProgressPercent;
        private set => SetProperty(ref _downloadProgressPercent, value);
    }

    public string DownloadProgressText {
        get => _downloadProgressText;
        private set => SetProperty(ref _downloadProgressText, value);
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default, bool surfaceFailures = true) {
        if (IsBusy) {
            return;
        }

        IsChecking = true;
        ErrorText = string.Empty;
        StatusText = "Checking for updates...";

        try {
            var result = await AutoUpdater.IsUpdateAvailableAsync(
                cancellationToken,
                includeSkippedUpdates: true);

            AvailableUpdate = result.Update;
            IsAvailableUpdateSkipped = result.Update is not null && AutoUpdater.IsUpdateSkipped(result.Update);

            if (result.IsUpdateAvailable && result.Update is not null) {
                StatusText = IsAvailableUpdateSkipped
                    ? $"Update {result.Update.VersionText} is skipped."
                    : $"Update {result.Update.VersionText} is available.";
            } else {
                StatusText = result.Reason ?? "No compatible update is available.";
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            StatusText = "Update check cancelled.";
        } catch (Exception ex) {
            Debug.WriteLine($"[NuraDesktop.Update] Update check failed: {ex}");
            AvailableUpdate = null;
            IsAvailableUpdateSkipped = false;

            if (surfaceFailures) {
                ErrorText = "Unable to check for updates. Try again later.";
                StatusText = ErrorText;
            } else {
                StatusText = "Updates could not be checked.";
            }
        } finally {
            IsChecking = false;
        }
    }

    public void SkipAvailableUpdate() {
        if (AvailableUpdate is null) {
            return;
        }

        try {
            AutoUpdater.SkipUpdate(AvailableUpdate);
            IsAvailableUpdateSkipped = true;
            StatusText = $"Update {AvailableUpdate.VersionText} is skipped.";
        } catch (Exception ex) {
            Debug.WriteLine($"[NuraDesktop.Update] Unable to persist skipped update: {ex}");
            ErrorText = "Unable to save the skipped update setting.";
            StatusText = ErrorText;
        }
    }

    public void ResumeUpdateAlerts() {
        try {
            AutoUpdater.ClearSkippedUpdate();
            IsAvailableUpdateSkipped = false;

            if (AvailableUpdate is not null) {
                StatusText = $"Update {AvailableUpdate.VersionText} is available.";
            }
        } catch (Exception ex) {
            Debug.WriteLine($"[NuraDesktop.Update] Unable to clear skipped update: {ex}");
            ErrorText = "Unable to resume update alerts.";
            StatusText = ErrorText;
        }
    }

    public void OpenAvailableRelease() {
        if (string.IsNullOrWhiteSpace(AvailableUpdate?.ReleaseUrl)) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo {
                FileName = AvailableUpdate.ReleaseUrl,
                UseShellExecute = true
            });
        } catch (Exception ex) {
            Debug.WriteLine($"[NuraDesktop.Update] Unable to open release page: {ex}");
            ErrorText = "Unable to open the release page.";
        }
    }

    public async Task DownloadAndInstallAsync(CancellationToken cancellationToken = default) {
        if (AvailableUpdate is null || IsBusy) {
            return;
        }

        IsDownloading = true;
        ErrorText = string.Empty;
        DownloadProgressPercent = 0;
        DownloadProgressText = "Preparing download...";
        StatusText = $"Downloading update {AvailableUpdate.VersionText}...";

        try {
            var downloaded = await AutoUpdater.DownloadUpdateAsync(
                AvailableUpdate,
                new Progress<UpdateDownloadProgress>(UpdateProgress),
                cancellationToken);

            DownloadProgressPercent = 100;
            DownloadProgressText = "Installing and restarting...";
            StatusText = "Installing update and restarting NuraDesktop...";
            AutoUpdater.InstallAndRestart(downloaded);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            StatusText = "Update download cancelled.";
        } catch (Exception ex) {
            Debug.WriteLine($"[NuraDesktop.Update] Update install failed: {ex}");
            ErrorText = $"Update failed: {ex.Message}";
            StatusText = ErrorText;
        } finally {
            IsDownloading = false;
        }
    }

    private void UpdateProgress(UpdateDownloadProgress progress) {
        DownloadProgressPercent = progress.Percent is { } percent
            ? Math.Clamp(percent, 0, 100)
            : 0;
        DownloadProgressText = progress.Percent is { } knownPercent
            ? $"Downloading {knownPercent:0.0}%"
            : "Downloading update...";
    }
}
