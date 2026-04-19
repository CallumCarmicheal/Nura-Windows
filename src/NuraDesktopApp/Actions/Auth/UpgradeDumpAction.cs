using NuraDesktopConsole.Config;
using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthUpgradeDump : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var upgradeEndpoint = (ArgumentReader.OptionalValue(args, "--upgrade-endpoint") ?? "upgrade").Trim();
        var language = (ArgumentReader.OptionalValue(args, "--language") ?? "en").Trim().ToLowerInvariant();
        var bluetoothSessionOverride = AuthStateSupport.ParseOptionalInt32(args, "--session");
        var firmwareVersionOverride = AuthStateSupport.ParseOptionalInt32(args, "--firmware-version");
        var outputRootOverride = ArgumentReader.OptionalValue(args, "--output-dir");

        var dumpResult = await AutomatedEntryDumpSupport.ExecuteAsync(
            logPrefix: "auth.upgrade_dump",
            outputPrefix: "upgrade_dump",
            endpoint: upgradeEndpoint,
            language: language,
            bluetoothSessionOverride: bluetoothSessionOverride,
            firmwareVersionOverride: firmwareVersionOverride,
            outputRootOverride: outputRootOverride,
            logger: logger,
            cancellationToken: cts.Token);

        return dumpResult.ExitCode;
    }
}
