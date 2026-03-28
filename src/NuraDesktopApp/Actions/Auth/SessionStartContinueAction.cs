using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSessionStartContinue : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        var endpoint = ArgumentReader.OptionalValue(args, "--endpoint")
            ?? SessionStartResponseParser.Parse(state.LastResponseBody ?? [])?.FinalEvent
            ?? "session/start_1";
        var sessionId = AuthStateSupport.ParseOptionalInt32(args, "--session") ?? state.BluetoothSessionId
            ?? throw new InvalidOperationException("session is required; run `auth session-start` first or pass --session explicitly");
        var responseHex = ArgumentReader.RequiredValue(args, "--response-hex");
        var inputBytes = Hex.Parse(responseHex);
        var responseBytes = NormalizeHeadsetResponseBytes(inputBytes, out var responseKind);

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.session_continue.endpoint={endpoint}");
        logger.WriteLine($"auth.session_continue.session={sessionId}");
        logger.WriteLine($"auth.session_continue.response.input_kind={responseKind}");
        logger.WriteLine($"auth.session_continue.response.input_hex={Hex.Format(inputBytes)}");
        logger.WriteLine($"auth.session_continue.response.hex={Hex.Format(responseBytes)}");
        logger.WriteLine($"auth.session_continue.response.base64={Convert.ToBase64String(responseBytes)}");
        AuthStateSupport.LogSessionState(state, logger);

        var packets = new IReadOnlyDictionary<string, object?>[] {
            new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["e"] = false,
                ["a"] = false,
                ["b"] = responseBytes,
                ["m"] = false
            }
        };

        using var client = new NuraAuthApiClient(logger);
        var result = await client.AutomatedEntryAsync(state, endpoint, sessionId, packets, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        if (result.DecodedBody is not null) {
            logger.WriteLine($"auth.session_continue.response.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
            LogAutomatedEntryDetails(result.DecodedBody, logger);
        }

        logger.WriteLine($"auth.session_continue.result_status={result.StatusCode}");
        logger.WriteLine($"auth.session_continue.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }

    private static void LogAutomatedEntryDetails(Dictionary<string, object?> responseBody, SessionLogger logger) {
        var details = SessionStartResponseParser.Parse(responseBody);
        if (details is null) {
            return;
        }
        SessionStartExecutionSupport.LogParsedDetails("auth.session_continue", details, logger);
    }

    private static byte[] NormalizeHeadsetResponseBytes(byte[] bytes, out string kind) {
        if (LooksLikeGaiaFrame(bytes)) {
            kind = "gaia_frame";
            return Library.Protocol.GaiaResponse.Parse(bytes).Data;
        }

        if (LooksLikeGaiaData(bytes)) {
            kind = "gaia_data";
            return bytes;
        }

        kind = "raw_bytes";
        return bytes;
    }

    private static bool LooksLikeGaiaFrame(byte[] bytes) {
        if (bytes.Length < 8 || bytes[0] != 0xff) {
            return false;
        }

        var length = bytes[3];
        return bytes.Length == length + 8;
    }

    private static bool LooksLikeGaiaData(byte[] bytes) {
        return bytes.Length >= 5 &&
               bytes[0] == 0x68 &&
               bytes[1] == 0x72 &&
               (bytes[2] & 0x80) == 0x80;
    }
}
