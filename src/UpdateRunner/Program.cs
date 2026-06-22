using System.Diagnostics;

namespace UpdateRunner;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

internal static class Program {
    private static int Main(string[] args) {
        try {
            Dictionary<string, string> options = ParseArgs(args);

            string sourceDir = Require(options, "--source");
            string targetDir = Require(options, "--target");
            string restartExe = Require(options, "--restart");

            if (options.TryGetValue("--parent-pid", out string? pidText) &&
                int.TryParse(pidText, out int parentPid)) {
                WaitForProcessToExit(parentPid, TimeSpan.FromSeconds(60));
            }

            if (options.TryGetValue("--wait-exes", out string? waitExeText)) {
                string[] exeNames = waitExeText
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                WaitForExecutablesInFolderToExit(exeNames, targetDir, TimeSpan.FromSeconds(60));
            }

            CopyDirectoryOver(sourceDir, targetDir);

            if (options.TryGetValue("--manifest", out string? manifestPath) &&
                !string.IsNullOrWhiteSpace(manifestPath) &&
                File.Exists(manifestPath)) {
                File.Delete(manifestPath);
            }

            string restartPath = Path.Combine(targetDir, restartExe);

            Process.Start(new ProcessStartInfo {
                FileName = restartPath,
                WorkingDirectory = targetDir,
                UseShellExecute = true
            });

            return 0;
        } catch (Exception ex) {
            string logPath = Path.Combine(
                Path.GetTempPath(),
                "Nura-Windows-Updater-Error.txt");

            File.WriteAllText(logPath, ex.ToString());
            return 1;
        }
    }

    private static void WaitForProcessToExit(int pid, TimeSpan timeout) {
        try {
            using Process process = Process.GetProcessById(pid);

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                throw new TimeoutException($"Process {pid} did not exit in time.");
        } catch (ArgumentException) {
            // Already exited.
        }
    }

    private static void WaitForExecutablesInFolderToExit(
        string[] exeNames,
        string targetDir,
        TimeSpan timeout) {
        DateTime deadline = DateTime.UtcNow + timeout;

        string normalizedTargetDir = Path.GetFullPath(targetDir)
            .TrimEnd(Path.DirectorySeparatorChar)
            .ToUpperInvariant();

        while (DateTime.UtcNow < deadline) {
            bool anyStillRunning = false;

            foreach (string exeName in exeNames) {
                string processName = Path.GetFileNameWithoutExtension(exeName);

                foreach (Process process in Process.GetProcessesByName(processName)) {
                    using (process) {
                        string? processPath = TryGetProcessPath(process);

                        if (string.IsNullOrWhiteSpace(processPath))
                            continue;

                        string processDir = Path.GetDirectoryName(Path.GetFullPath(processPath))!
                            .TrimEnd(Path.DirectorySeparatorChar)
                            .ToUpperInvariant();

                        if (processDir == normalizedTargetDir) {
                            anyStillRunning = true;
                            break;
                        }
                    }
                }

                if (anyStillRunning)
                    break;
            }

            if (!anyStillRunning)
                return;

            Thread.Sleep(500);
        }

        throw new TimeoutException(
            "NuraDesktop.exe or NuraTerm.exe is still running, so the update could not continue.");
    }

    private static string? TryGetProcessPath(Process process) {
        try {
            return process.MainModule?.FileName;
        } catch {
            return null;
        }
    }

    private static void CopyDirectoryOver(string sourceDir, string targetDir) {
        Directory.CreateDirectory(targetDir);

        foreach (string directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(sourceDir, directory);
            string targetPath = Path.Combine(targetDir, relativePath);

            Directory.CreateDirectory(targetPath);
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            string relativePath = Path.GetRelativePath(sourceDir, file);
            string targetPath = Path.Combine(targetDir, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length - 1; i += 2) {
            result[args[i]] = args[i + 1];
        }

        return result;
    }

    private static string Require(Dictionary<string, string> options, string name) {
        if (!options.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required argument: {name}");

        return value;
    }
}
