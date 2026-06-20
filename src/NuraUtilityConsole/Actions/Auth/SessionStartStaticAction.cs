using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSessionStartStatic : IAction {
    private static readonly string[] Endpoints = [
        "session/start_1",
        "session/start_2",
        "session/start_3",
        "session/start_4"
    ];

    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var bluetoothSessionOverride = AuthStateSupport.ParseOptionalInt32(args, "--session");
        var outputRootOverride = ArgumentReader.OptionalValue(args, "--output-dir");
        var logPath = ArgumentReader.RequiredValue(args, "--log-path");

        logger.WriteLine($"auth.session_start_static.log_path={Path.GetFullPath(logPath)}");
        logger.WriteLine($"auth.session_start_static.session_override={(bluetoothSessionOverride is null ? string.Empty : bluetoothSessionOverride.Value.ToString())}");

        foreach (var endpoint in Endpoints) {
            var packets = ResolvePacketsForEndpoint(endpoint, logPath, logger);
            var stepOutputRoot = string.IsNullOrWhiteSpace(outputRootOverride)
                ? null
                : Path.Combine(Path.GetFullPath(outputRootOverride), endpoint.Replace('/', '_'));

            var result = await AutomatedEntryDumpSupport.ExecuteAsync(
                logPrefix: "auth.session_start_static",
                outputPrefix: "session_start_static",
                endpoint: endpoint,
                additionalPayload: null,
                packets: packets,
                bluetoothSessionOverride: bluetoothSessionOverride,
                firmwareVersionOverride: null,
                outputRootOverride: stepOutputRoot,
                logger: logger,
                cancellationToken: cts.Token);

            logger.WriteLine($"auth.session_start_static.step.endpoint={endpoint}");
            logger.WriteLine($"auth.session_start_static.step.exit_code={result.ExitCode}");
            logger.WriteLine($"auth.session_start_static.step.output_dir={result.OutputDirectory}");

            if (result.ExitCode != 0) {
                return result.ExitCode;
            }
        }

        logger.WriteLine("auth.session_start_static.result=verified_session_replayed");
        return 0;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ResolvePacketsForEndpoint(
        string endpoint,
        string logPath,
        SessionLogger logger) {
        if (!string.Equals(endpoint, "session/start_2", StringComparison.OrdinalIgnoreCase)) {
            return SessionStartStaticCapture.LoadPackets(endpoint, logPath, logger);
        }

        logger.WriteLine("auth.session_start_static.capture.mode=session_start_2_synthesized");
        return BuildSessionStart2PacketsFromCurrentState(logger);
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> BuildSessionStart2PacketsFromCurrentState(SessionLogger logger) {
        var state = LocalStateFiles.LoadAuthState(logger);
        var details = SessionStartResponseParser.Parse(state.LastResponseBody ?? [])
            ?? throw new InvalidOperationException("current auth state does not contain a parsed session/start_1 response");

        var packet = details.Packets.FirstOrDefault()
            ?? throw new InvalidOperationException("session/start_1 response did not include a packet");

        var responsePayload = packet.PayloadBytes
            ?? throw new InvalidOperationException("session/start_1 packet bytes are missing");

        if (responsePayload.Length != 32 || responsePayload[0] != 0x68 || responsePayload[1] != 0x72 || responsePayload[2] != 0x00 || responsePayload[3] != 0x05) {
            throw new InvalidOperationException("session/start_1 payload is not the expected 0x0005 challenge shape");
        }

        var nonce = responsePayload[4..16];
        var challenge = responsePayload[16..32];

        var config = LocalStateFiles.LoadConfig(logger);
        var runtime = SessionRuntime.Create(config.DeviceKey, nonce);
        var gmac = runtime.Crypto.GenerateChallengeResponse(challenge);
        var replayPayload = ByteArray.Combine(
            [0x68, 0x72, 0x80, 0x05, 0x00],
            gmac);

        logger.WriteLine($"auth.session_start_static.capture.session_start_2.nonce.hex={Hex.Format(nonce)}");
        logger.WriteLine($"auth.session_start_static.capture.session_start_2.challenge.hex={Hex.Format(challenge)}");
        logger.WriteLine($"auth.session_start_static.capture.session_start_2.response_gmac.hex={Hex.Format(gmac)}");
        logger.WriteLine($"auth.session_start_static.capture.session_start_2.payload.hex={Hex.Format(replayPayload)}");

        return [
            new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["e"] = false,
                ["a"] = false,
                ["b"] = replayPayload
            }
        ];
    }
}
