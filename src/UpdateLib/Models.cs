using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace UpdateLib;

public enum AppInstallKind {
    FrameworkDependent,
    SelfContained
}

public sealed class AutoUpdaterOptions {
    public string ProductName { get; set; } = "Nura-Windows";

    public string GitHubOwner { get; set; } = "CallumCarmicheal";

    public string GitHubRepository { get; set; } = "Nura-Windows";

    public bool IncludePrereleases { get; set; } = true;

    public bool RespectSkippedUpdates { get; set; } = true;

    public string UpdaterExeName { get; set; } = "UpdateRunner.exe";

    public string[] AppExecutablesToWaitFor { get; set; } = [
        "NuraDesktop.exe",
        "NuraTerm.exe"
    ];
}

public sealed class UpdateCheckResult {
    public bool IsUpdateAvailable { get; init; }

    public string CurrentVersionText { get; init; } = "";

    public string CurrentExecutableName { get; init; } = "";

    public UpdateInfo? Update { get; init; }

    public string? Reason { get; init; }

    public static UpdateCheckResult NotAvailable(string currentVersionText, string reason) {
        return new UpdateCheckResult {
            IsUpdateAvailable = false,
            CurrentVersionText = currentVersionText,
            Reason = reason
        };
    }
}

public sealed class UpdateInfo {
    public string VersionText { get; set; } = "";

    public string TagName { get; set; } = "";

    public string ReleaseName { get; set; } = "";

    public string ReleaseUrl { get; set; } = "";

    public bool IsPrerelease { get; set; }

    public string AssetName { get; set; } = "";

    public string AssetDownloadUrl { get; set; } = "";

    public string? AssetDigest { get; set; }

    public AppInstallKind InstallKind { get; set; }

    public string RuntimeIdentifier { get; set; } = "";
}

public sealed class DownloadedUpdate {
    public UpdateInfo Update { get; set; } = new();

    public string UpdateRoot { get; set; } = "";

    public string ZipPath { get; set; } = "";

    public string ExtractedPayloadDirectory { get; set; } = "";

    public string InstallerPath { get; set; } = "";

    public string InstallDirectory { get; set; } = "";

    public string DownloadedByExecutableName { get; set; } = "";

    public DateTimeOffset DownloadedAtUtc { get; set; }
}

public sealed class UpdateDownloadProgress {
    public long BytesReceived { get; init; }

    public long? TotalBytes { get; init; }

    public TimeSpan Elapsed { get; init; }

    public double BytesPerSecond { get; init; }

    public double? Percent =>
        TotalBytes is > 0
            ? BytesReceived / (double)TotalBytes.Value * 100.0
            : null;

    public TimeSpan? EstimatedTimeRemaining {
        get {
            if (TotalBytes is not > 0)
                return null;

            if (BytesPerSecond <= 0)
                return null;

            long remainingBytes = TotalBytes.Value - BytesReceived;

            if (remainingBytes <= 0)
                return TimeSpan.Zero;

            return TimeSpan.FromSeconds(remainingBytes / BytesPerSecond);
        }
    }

    public bool IsComplete =>
        TotalBytes is > 0 && BytesReceived >= TotalBytes.Value;
}