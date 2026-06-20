using NuraDesktopConsole.Config;
using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthLanguageDumpStatic : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var endpoint = (ArgumentReader.OptionalValue(args, "--endpoint") ?? "change_language_1").Trim();
        var language = (ArgumentReader.OptionalValue(args, "--language") ?? "ja").Trim().ToLowerInvariant();
        var bluetoothSessionOverride = AuthStateSupport.ParseOptionalInt32(args, "--session");
        var firmwareVersionOverride = AuthStateSupport.ParseOptionalInt32(args, "--firmware-version");
        var outputRootOverride = ArgumentReader.OptionalValue(args, "--output-dir");

        IReadOnlyDictionary<string, object?>? additionalPayload = null;
        IReadOnlyList<IReadOnlyDictionary<string, object?>> packets = [];

        if (string.Equals(endpoint, "change_language", StringComparison.OrdinalIgnoreCase)) {
            additionalPayload = new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["language"] = language
            };
        } else {
            var logPath = ArgumentReader.RequiredValue(args, "--log-path");
            packets = LanguageDumpStaticCapture.LoadPackets(endpoint, logPath, logger);
        }

        var dumpResult = await AutomatedEntryDumpSupport.ExecuteAsync(
            logPrefix: "auth.language_dump_static",
            outputPrefix: "language_dump_static",
            endpoint: endpoint,
            additionalPayload: additionalPayload,
            packets: packets,
            bluetoothSessionOverride: bluetoothSessionOverride,
            firmwareVersionOverride: firmwareVersionOverride,
            outputRootOverride: outputRootOverride,
            logger: logger,
            cancellationToken: cts.Token);

        return dumpResult.ExitCode;
    }
}
