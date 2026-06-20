#:property TargetFramework=net10.0
#:property Nullable=enable
#:property PublishAot=false

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

var root = FindRepositoryRoot();
Directory.SetCurrentDirectory(root);

var options = Options.Parse(args);

if (options.ShowHelp) {
    PrintHelp();
    return 0;
}

try {
    switch (options.Command) {
        case "release":
            await BuildReleaseAsync(options);
            break;
        case "clean":
            Clean();
            break;
        default:
            throw new InvalidOperationException($"Unknown command '{options.Command}'. Run: dotnet run --file build.cs -- --help");
    }

    return 0;
}
catch (Exception ex) {
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Build failed: {ex.Message}");
    return 1;
}

static async Task BuildReleaseAsync(Options options) {
    var version = ResolveVersion(options.Version);

    Clean();

    Console.WriteLine($"Creating Nura Windows release v{version} ({options.Rid}, {options.Configuration})...");

    await RunAsync("dotnet", ["restore", "NuraDesktopApp.slnx", "-p:EnableWindowsTargeting=true"]);

    if (!options.SkipTests && Directory.Exists("tests/NuraLib.Tests")) {
        await RunAsync("dotnet", [
            "run",
            "--project", "tests/NuraLib.Tests/NuraLib.Tests.csproj",
            "-c", options.Configuration,
            "--no-restore",
            "-p:EnableWindowsTargeting=true"
        ]);
    }

    var releaseRoot = Path.Combine("artifacts", "release");
    Directory.CreateDirectory(releaseRoot);

    await CreateZipAsync(
        options,
        version,
        selfContained: true,
        zipName: $"Nura-Windows-v{version}-{options.Rid}.zip");

    await CreateZipAsync(
        options,
        version,
        selfContained: false,
        zipName: $"Nura-Windows-v{version}-{options.Rid}-fxdependent.zip");

    Console.WriteLine();
    Console.WriteLine("Release artifacts:");
    Console.WriteLine(Path.GetFullPath(Path.Combine(releaseRoot, $"Nura-Windows-v{version}-{options.Rid}.zip")));
    Console.WriteLine(Path.GetFullPath(Path.Combine(releaseRoot, $"Nura-Windows-v{version}-{options.Rid}-fxdependent.zip")));
}

static async Task CreateZipAsync(Options options, string version, bool selfContained, string zipName) {
    var flavor = selfContained ? "self-contained" : "fxdependent";
    var tempRoot = Path.Combine("artifacts", "publish-temp", flavor);
    var stage = Path.Combine("artifacts", "staging", flavor, $"Nura-Windows-v{version}-{options.Rid}{(selfContained ? string.Empty : "-fxdependent")}");
    var zipPath = Path.Combine("artifacts", "release", zipName);

    Directory.CreateDirectory(tempRoot);
    Directory.CreateDirectory(stage);

    var projects = new[] {
        new PublishProject("NuraApp", "src/NuraApp/NuraApp.csproj"),
        new PublishProject("NuraPopupWpf", "src/NuraPopupWpf/NuraPopupWpf.csproj")
    };

    foreach (var project in projects) {
        var projectOut = Path.Combine(tempRoot, project.Name);
        Directory.CreateDirectory(projectOut);

        var publishArgs = new List<string> {
            "publish", project.Path,
            "-c", options.Configuration,
            "-r", options.Rid,
            selfContained ? "--self-contained" : "--no-self-contained",
            "-o", projectOut,
            "-p:EnableWindowsTargeting=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false",
            "-p:UseAppHost=true"
        };

        await RunAsync("dotnet", publishArgs);
        MergeDirectory(projectOut, stage);
    }

    CopyRepoFileIfExists("README.md", stage);
    CopyRepoFileIfExists("LICENSE.md", stage);
    CopyRepoFileIfExists("NOTICE", stage);

    if (File.Exists(zipPath)) {
        File.Delete(zipPath);
    }

    ZipFile.CreateFromDirectory(stage, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    Console.WriteLine($"Created {Path.GetFullPath(zipPath)}");
}

static void Clean() {
    DeleteDirectoryIfExists(Path.Combine("artifacts", "release"));
    DeleteDirectoryIfExists(Path.Combine("artifacts", "staging"));
    DeleteDirectoryIfExists(Path.Combine("artifacts", "publish-temp"));
}

static void MergeDirectory(string source, string destination) {
    foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
        var relative = Path.GetRelativePath(source, sourceFile);
        var destinationFile = Path.Combine(destination, relative);
        var destinationDirectory = Path.GetDirectoryName(destinationFile);

        if (!string.IsNullOrEmpty(destinationDirectory)) {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationFile)) {
            if (!FilesAreIdentical(sourceFile, destinationFile)) {
                throw new InvalidOperationException(
                    $"Publish output conflict for '{relative}'. The two application publishes produced different files with the same name.");
            }

            continue;
        }

        File.Copy(sourceFile, destinationFile);
    }
}

static bool FilesAreIdentical(string left, string right) {
    var leftInfo = new FileInfo(left);
    var rightInfo = new FileInfo(right);

    if (leftInfo.Length != rightInfo.Length) {
        return false;
    }

    using var leftStream = File.OpenRead(left);
    using var rightStream = File.OpenRead(right);
    var leftHash = SHA256.HashData(leftStream);
    var rightHash = SHA256.HashData(rightStream);
    return leftHash.SequenceEqual(rightHash);
}

static void CopyRepoFileIfExists(string relativePath, string stage) {
    if (!File.Exists(relativePath)) {
        return;
    }

    var target = Path.Combine(stage, relativePath);
    var targetDirectory = Path.GetDirectoryName(target);

    if (!string.IsNullOrEmpty(targetDirectory)) {
        Directory.CreateDirectory(targetDirectory);
    }

    File.Copy(relativePath, target, overwrite: true);
}

static async Task RunAsync(string fileName, IReadOnlyList<string> arguments) {
    Console.WriteLine($"> {fileName} {string.Join(' ', arguments.Select(QuoteIfNeeded))}");

    var startInfo = new ProcessStartInfo {
        FileName = fileName,
        UseShellExecute = false
    };

    foreach (var argument in arguments) {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
    await process.WaitForExitAsync();

    if (process.ExitCode != 0) {
        throw new InvalidOperationException($"'{fileName}' exited with code {process.ExitCode}.");
    }
}

static string QuoteIfNeeded(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

static string ResolveVersion(string? explicitVersion) {
    if (!string.IsNullOrWhiteSpace(explicitVersion)) {
        return NormalizeVersion(explicitVersion);
    }

    var gitVersion = TryReadGitVersion();
    if (!string.IsNullOrWhiteSpace(gitVersion)) {
        return NormalizeVersion(gitVersion);
    }

    throw new InvalidOperationException("No release version was provided. Use: dotnet run --file build.cs -- release --version 1.2.3");
}

static string? TryReadGitVersion() {
    try {
        var startInfo = new ProcessStartInfo {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[] { "describe", "--tags", "--abbrev=0", "--match", "v[0-9]*" }) {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null) {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : null;
    }
    catch {
        return null;
    }
}

static string NormalizeVersion(string version) {
    version = version.Trim();

    if (version.StartsWith('v') || version.StartsWith('V')) {
        version = version[1..];
    }

    if (!Regex.IsMatch(version, "^[0-9A-Za-z][0-9A-Za-z._+-]*$")) {
        throw new InvalidOperationException($"Invalid version '{version}'. Use something filename-safe like 1.2.3 or 1.2.3-beta.1.");
    }

    return version;
}

static string FindRepositoryRoot() {
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null) {
        if (File.Exists(Path.Combine(current.FullName, "NuraDesktopApp.slnx"))) {
            return current.FullName;
        }

        current = current.Parent;
    }

    current = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (current is not null) {
        if (File.Exists(Path.Combine(current.FullName, "NuraDesktopApp.slnx"))) {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new InvalidOperationException("Could not find NuraDesktopApp.slnx. Run this from inside the repository.");
}

static void DeleteDirectoryIfExists(string path) {
    if (Directory.Exists(path)) {
        Directory.Delete(path, recursive: true);
    }
}

static void PrintHelp() {
    Console.WriteLine("""
    Nura release build

    Usage:
      dotnet run --file build.cs -- release --version 1.2.3
      dotnet run --file build.cs -- release --version 1.2.3 --skip-tests
      dotnet run --file build.cs -- clean

    Outputs:
      artifacts/release/Nura-Windows-v{version}-win-x64.zip
      artifacts/release/Nura-Windows-v{version}-win-x64-fxdependent.zip
    """);
}

sealed record PublishProject(string Name, string Path);

sealed record Options(
    string Command,
    string? Version,
    string Configuration,
    string Rid,
    bool SkipTests,
    bool ShowHelp) {

    public static Options Parse(string[] args) {
        var command = "release";
        var version = default(string);
        var configuration = "Release";
        var rid = "win-x64";
        var skipTests = false;
        var showHelp = false;

        var index = 0;
        if (args.Length > 0 && !args[0].StartsWith('-')) {
            command = args[0];
            index = 1;
        }

        while (index < args.Length) {
            var arg = args[index++];
            switch (arg) {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--version":
                case "-v":
                    version = RequireValue(args, ref index, arg);
                    break;
                case "--configuration":
                case "-c":
                    configuration = RequireValue(args, ref index, arg);
                    break;
                case "--rid":
                case "-r":
                    rid = RequireValue(args, ref index, arg);
                    break;
                case "--skip-tests":
                case "--no-tests":
                    skipTests = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown option '{arg}'. Run with --help for usage.");
            }
        }

        return new Options(command, version, configuration, rid, skipTests, showHelp);
    }

    private static string RequireValue(string[] args, ref int index, string optionName) {
        if (index >= args.Length) {
            throw new InvalidOperationException($"Missing value for {optionName}.");
        }

        return args[index++];
    }
}
