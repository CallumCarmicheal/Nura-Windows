using NuraDesktopConsole.Config;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura;

internal static class LocalStateFiles {
    internal static NuraOfflineConfig LoadConfig(SessionLogger logger) {
        var configPath = ResolveConfigPath();
        logger.WriteLine($"config.path={configPath}");
        return NuraOfflineConfig.Load(configPath);
    }

    internal static string LoadAuthPath(SessionLogger logger) {
        var authPath = ResolveAuthStatePath();
        logger.WriteLine($"auth.path={authPath}");
        return authPath;
    }

    internal static NuraAuthState LoadAuthState(SessionLogger logger) {
        var authPath = LoadAuthPath(logger);
        return NuraAuthState.LoadOrCreate(authPath);
    }

    private static string ResolveConfigPath() {
        var candidates = new[] {
            Path.Combine(Directory.GetCurrentDirectory(), "nura-config.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "NuraDesktopApp", "nura-config.json"),
            Path.Combine(AppContext.BaseDirectory, "nura-config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "nura-config.json"),
        };

        var normalizedCandidates = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var match = normalizedCandidates.FirstOrDefault(File.Exists);
        if (match is null) {
            throw new FileNotFoundException("nura-config.json not found in the working directory or app directory");
        }

        return match;
    }

    private static string ResolveAuthStatePath() {
        var candidates = new[] {
            Path.Combine(Directory.GetCurrentDirectory(), "nura-auth.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "NuraDesktopApp", "nura-auth.json"),
            Path.Combine(AppContext.BaseDirectory, "nura-auth.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "nura-auth.json")
        };

        var normalizedCandidates = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existingMatch = normalizedCandidates.FirstOrDefault(File.Exists);
        if (existingMatch is not null) {
            return existingMatch;
        }

        var projectScopedMatch = normalizedCandidates.FirstOrDefault(path =>
            path.Contains($"{Path.DirectorySeparatorChar}desktop-app{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        if (projectScopedMatch is not null) {
            return projectScopedMatch;
        }

        return normalizedCandidates.First();
    }
}
