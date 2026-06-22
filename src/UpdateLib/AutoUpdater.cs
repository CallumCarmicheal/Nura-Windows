using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using NuGet.Versioning;

namespace UpdateLib;

public enum InstallKind {
    FrameworkDependent,
    SelfContained
}

public static class AutoUpdater {
    private static readonly HttpClient Http = new();

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true
    };

    public static AutoUpdaterOptions Options { get; set; } = new();

    public static async Task<UpdateCheckResult> IsUpdateAvailableAsync(
        CancellationToken cancellationToken = default,
        bool includeSkippedUpdates = false) {
        string installDir = GetInstallDirectory();
        string currentExeName = GetCurrentExeName();
        string currentVersionText = GetCurrentVersionText();

        if (!NuGetVersion.TryParse(CleanVersion(currentVersionText), out NuGetVersion? currentVersion)) {
            return UpdateCheckResult.NotAvailable(
                currentVersionText,
                $"Current app version '{currentVersionText}' is not a valid NuGet/SemVer version.");
        }

        IReadOnlyList<GitHubRelease> releases = await GetReleasesAsync(
            currentVersion,
            cancellationToken);

        AppInstallKind installKind = DetectInstallKind(installDir);
        string rid = GetCurrentRid();

        List<UpdateInfo> candidates = [];

        foreach (GitHubRelease release in releases) {
            if (release.Draft)
                continue;

            if (release.Prerelease && !Options.IncludePrereleases)
                continue;

            if (!NuGetVersion.TryParse(CleanVersion(release.TagName), out NuGetVersion? releaseVersion))
                continue;

            if (VersionComparer.VersionRelease.Compare(releaseVersion, currentVersion) <= 0)
                continue;

            GitHubAsset? asset = TrySelectUpdateAsset(release.Assets, installKind, rid);

            if (asset is null)
                continue;

            candidates.Add(new UpdateInfo {
                VersionText = releaseVersion.ToNormalizedString(),
                TagName = release.TagName,
                ReleaseName = release.Name,
                ReleaseUrl = release.HtmlUrl,
                IsPrerelease = release.Prerelease,
                AssetName = asset.Name,
                AssetDownloadUrl = asset.BrowserDownloadUrl,
                AssetDigest = asset.Digest,
                InstallKind = installKind,
                RuntimeIdentifier = rid
            });
        }

        UpdateInfo? latest = candidates
            .OrderByDescending(x => NuGetVersion.Parse(x.VersionText), VersionComparer.VersionRelease)
            .FirstOrDefault();

        if (latest is null) {
            return UpdateCheckResult.NotAvailable(
                currentVersionText,
                "No newer compatible release was found.");
        }

        if (Options.RespectSkippedUpdates && !includeSkippedUpdates) {
            string? skippedVersion = ReadSkippedVersion();

            if (string.Equals(skippedVersion, latest.VersionText, StringComparison.OrdinalIgnoreCase)) {
                return UpdateCheckResult.NotAvailable(
                    currentVersionText,
                    $"Update {latest.VersionText} is currently skipped.");
            }
        }

        return new UpdateCheckResult {
            IsUpdateAvailable = true,
            CurrentVersionText = currentVersionText,
            Update = latest,
            Reason = null,
            CurrentExecutableName = currentExeName
        };
    }

    public static async Task<DownloadedUpdate> DownloadUpdateAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    ) {
        string installDir = GetInstallDirectory();
        string currentExeName = GetCurrentExeName();

        string updateRoot = Path.Combine(
            GetUpdateCacheDirectory(),
            $"{SanitizeFileName(update.VersionText)}-{Guid.NewGuid():N}");

        string zipPath = Path.Combine(updateRoot, "update.zip");
        string extractDir = Path.Combine(updateRoot, "extracted");

        Directory.CreateDirectory(updateRoot);
        Directory.CreateDirectory(extractDir);

        await DownloadFileAsync(
            update.AssetDownloadUrl,
            zipPath,
            progress,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(update.AssetDigest))
            VerifySha256(zipPath, update.AssetDigest);

        SafeExtractZip(zipPath, extractDir);

        string payloadDir = FindPayloadRoot(extractDir);

        string installedUpdaterPath = Path.Combine(installDir, Options.UpdaterExeName);
        string cachedUpdaterPath = Path.Combine(updateRoot, Options.UpdaterExeName);

        if (!File.Exists(installedUpdaterPath))
            throw new FileNotFoundException("Updater executable was not found.", installedUpdaterPath);

        File.Copy(installedUpdaterPath, cachedUpdaterPath, overwrite: true);

        var downloaded = new DownloadedUpdate {
            Update = update,
            UpdateRoot = updateRoot,
            ZipPath = zipPath,
            ExtractedPayloadDirectory = payloadDir,
            InstallerPath = cachedUpdaterPath,
            InstallDirectory = installDir,
            DownloadedByExecutableName = currentExeName,
            DownloadedAtUtc = DateTimeOffset.UtcNow
        };

        WriteDownloadedUpdateManifest(downloaded);

        return downloaded;
    }

    public static DownloadedUpdate? GetDownloadedUpdate() {
        string manifestPath = GetDownloadedUpdateManifestPath();

        if (!File.Exists(manifestPath))
            return null;

        try {
            string json = File.ReadAllText(manifestPath);
            DownloadedUpdate? update = JsonSerializer.Deserialize<DownloadedUpdate>(json, JsonOptions);

            if (update is null)
                return null;

            if (!Directory.Exists(update.ExtractedPayloadDirectory))
                return null;

            if (!File.Exists(update.InstallerPath))
                return null;

            return update;
        } catch {
            return null;
        }
    }

    public static void InstallAndRestart(DownloadedUpdate downloadedUpdate) {
        string restartExeName = GetCurrentExeName();

        if (!File.Exists(downloadedUpdate.InstallerPath))
            throw new FileNotFoundException("Cached updater executable was not found.", downloadedUpdate.InstallerPath);

        if (!Directory.Exists(downloadedUpdate.ExtractedPayloadDirectory))
            throw new DirectoryNotFoundException(downloadedUpdate.ExtractedPayloadDirectory);

        var startInfo = new ProcessStartInfo {
            FileName = downloadedUpdate.InstallerPath,
            WorkingDirectory = downloadedUpdate.UpdateRoot,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("--source");
        startInfo.ArgumentList.Add(downloadedUpdate.ExtractedPayloadDirectory);

        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(downloadedUpdate.InstallDirectory);

        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(restartExeName);

        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

        startInfo.ArgumentList.Add("--wait-exes");
        startInfo.ArgumentList.Add(string.Join(';', Options.AppExecutablesToWaitFor));

        startInfo.ArgumentList.Add("--manifest");
        startInfo.ArgumentList.Add(GetDownloadedUpdateManifestPath());

        Process.Start(startInfo);

        Environment.Exit(0);
    }

    public static void InstallAndRestart() {
        DownloadedUpdate? downloadedUpdate = GetDownloadedUpdate();

        if (downloadedUpdate is null)
            throw new InvalidOperationException("No downloaded update is available to install.");

        InstallAndRestart(downloadedUpdate);
    }

    public static void SkipUpdate(UpdateInfo update) {
        Directory.CreateDirectory(GetUpdateCacheDirectory());
        File.WriteAllText(GetSkippedVersionPath(), update.VersionText);
    }

    public static bool IsUpdateSkipped(UpdateInfo update) {
        ArgumentNullException.ThrowIfNull(update);
        return string.Equals(
            ReadSkippedVersion(),
            update.VersionText,
            StringComparison.OrdinalIgnoreCase);
    }

    public static void ClearSkippedUpdate() {
        string path = GetSkippedVersionPath();

        if (File.Exists(path))
            File.Delete(path);
    }

    public static void ClearDownloadedUpdate() {
        DownloadedUpdate? downloaded = GetDownloadedUpdate();

        if (downloaded is not null && Directory.Exists(downloaded.UpdateRoot)) {
            try {
                Directory.Delete(downloaded.UpdateRoot, recursive: true);
            } catch {
                // Best effort cleanup only.
            }
        }

        string manifestPath = GetDownloadedUpdateManifestPath();

        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(
        NuGetVersion currentVersion,
        CancellationToken cancellationToken) {
        string url = $"https://api.github.com/repos/{Options.GitHubOwner}/{Options.GitHubRepository}/releases";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(Options.ProductName, currentVersion.ToNormalizedString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        List<GitHubRelease>? releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(
            cancellationToken: cancellationToken);

        return releases ?? [];
    }

    private static GitHubAsset? TrySelectUpdateAsset(
        IReadOnlyList<GitHubAsset> assets,
        AppInstallKind installKind,
        string rid) {
        List<GitHubAsset> zipAssets = assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
            .ToList();

        static bool IsFxDependent(string name) {
            // Handle possible future name changes.
            return name.Contains("fxdependent", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("fx-depend", StringComparison.OrdinalIgnoreCase)
                   || name.Contains("fxdependant", StringComparison.OrdinalIgnoreCase);
        }

        return installKind switch {
            AppInstallKind.SelfContained => zipAssets
                .FirstOrDefault(a => !IsFxDependent(a.Name)),

            AppInstallKind.FrameworkDependent => zipAssets
                .FirstOrDefault(a => IsFxDependent(a.Name)),

            _ => null
        };
    }

    private static AppInstallKind DetectInstallKind(string installDir) {
        string[] selfContainedMarkers =
        [
            "createdump.exe",
            "coreclr.dll",
            "clrjit.dll",
            "hostfxr.dll",
            "hostpolicy.dll"
        ];

        foreach (string marker in selfContainedMarkers) {
            if (File.Exists(Path.Combine(installDir, marker)))
                return AppInstallKind.SelfContained;
        }

        return AppInstallKind.FrameworkDependent;
    }

    public static string GetCurrentVersionText() {
        Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

        string? version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(version))
            return version;

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static string GetInstallDirectory() {
        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static string GetCurrentExeName() {
        string? processPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("Could not determine current executable path.");

        return Path.GetFileName(processPath);
    }

    private static string GetCurrentRid() {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Nura updater currently only supports Windows.");

        return RuntimeInformation.ProcessArchitecture switch {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken
    ) {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue(Options.ProductName, "Updater"));

        using HttpResponseMessage response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        long receivedBytes = 0;

        var stopwatch = Stopwatch.StartNew();

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);

        await using FileStream output = new(
            destinationPath,
            new FileStreamOptions {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 1024 * 128,
                Options = FileOptions.Asynchronous
            });

        byte[] buffer = new byte[1024 * 128];

        while (true) {
            int read = await input.ReadAsync(buffer, cancellationToken);

            if (read == 0)
                break;

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            receivedBytes += read;

            double bytesPerSecond = stopwatch.Elapsed.TotalSeconds > 0
                ? receivedBytes / stopwatch.Elapsed.TotalSeconds
                : 0;

            progress?.Report(new UpdateDownloadProgress {
                BytesReceived = receivedBytes,
                TotalBytes = totalBytes,
                Elapsed = stopwatch.Elapsed,
                BytesPerSecond = bytesPerSecond
            });
        }

        stopwatch.Stop();

        progress?.Report(new UpdateDownloadProgress {
            BytesReceived = receivedBytes,
            TotalBytes = totalBytes,
            Elapsed = stopwatch.Elapsed,
            BytesPerSecond = stopwatch.Elapsed.TotalSeconds > 0
                ? receivedBytes / stopwatch.Elapsed.TotalSeconds
                : 0
        });
    }

    private static void VerifySha256(string filePath, string githubDigest) {
        const string prefix = "sha256:";

        if (!githubDigest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported digest format: {githubDigest}");

        string expectedHash = githubDigest[prefix.Length..];

        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(filePath);

        string actualHash = Convert
            .ToHexString(sha256.ComputeHash(stream))
            .ToLowerInvariant();

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Update failed SHA-256 check. Expected {expectedHash}, got {actualHash}.");
        }
    }

    private static void SafeExtractZip(string zipPath, string destinationDirectory) {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);

        string destinationRoot = Path.GetFullPath(destinationDirectory);

        foreach (ZipArchiveEntry entry in archive.Entries) {
            string destinationPath = Path.GetFullPath(
                Path.Combine(destinationRoot, entry.FullName));

            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ZIP contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static string FindPayloadRoot(string extractDir) {
        string[] files = Directory.GetFiles(extractDir);
        string[] directories = Directory.GetDirectories(extractDir);

        if (files.Length == 0 && directories.Length == 1)
            return directories[0];

        return extractDir;
    }

    private static string CleanVersion(string version) {
        version = version.Trim();

        if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            version = version[1..];

        return version;
    }

    private static string GetUpdateCacheDirectory() {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Options.ProductName,
            "Updates");
    }

    private static string GetSkippedVersionPath() {
        return Path.Combine(GetUpdateCacheDirectory(), "skipped-version.txt");
    }

    private static string? ReadSkippedVersion() {
        string path = GetSkippedVersionPath();

        if (!File.Exists(path))
            return null;

        return File.ReadAllText(path).Trim();
    }

    private static string GetDownloadedUpdateManifestPath() {
        return Path.Combine(GetUpdateCacheDirectory(), "downloaded-update.json");
    }

    private static void WriteDownloadedUpdateManifest(DownloadedUpdate downloadedUpdate) {
        Directory.CreateDirectory(GetUpdateCacheDirectory());

        string json = JsonSerializer.Serialize(downloadedUpdate, JsonOptions);
        File.WriteAllText(GetDownloadedUpdateManifestPath(), json);
    }

    private static string SanitizeFileName(string value) {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '-');

        return value;
    }
}
