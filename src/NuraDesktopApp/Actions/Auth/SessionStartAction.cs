using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSessionStart : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        var serialNumber = AuthStateSupport.ParseRequiredInt32(args, "--serial");
        var firmwareVersion = AuthStateSupport.ParseRequiredInt32(args, "--firmware-version");
        var maxPacketLength = AuthStateSupport.ParseOptionalInt32(args, "--max-packet-length") ?? 182;
        var maxBulkPacketLength = AuthStateSupport.ParseOptionalInt32(args, "--max-bulk-packet-length") ?? 0;
        var userSessionId = AuthStateSupport.ParseOptionalInt32(args, "--usid") ?? state.UserSessionId
            ?? throw new InvalidOperationException("usid is required; run `auth app-session`, pass --usid explicitly, or load auth state with a real userSessionId");

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.session_start.serial={serialNumber}");
        logger.WriteLine($"auth.session_start.firmware_version={firmwareVersion}");
        logger.WriteLine($"auth.session_start.max_packet_length={maxPacketLength}");
        logger.WriteLine($"auth.session_start.max_bulk_packet_length={maxBulkPacketLength}");
        logger.WriteLine($"auth.session_start.usid={userSessionId}");
        AuthStateSupport.LogSessionState(state, logger);
        var usidSource = ArgumentReader.OptionalValue(args, "--usid") is null
            ? "auth_state.user_session_id"
            : "explicit_argument";
        logger.WriteLine($"auth.session_start.usid_source={usidSource}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.SessionStartAsync(state, serialNumber, firmwareVersion, maxPacketLength, maxBulkPacketLength, userSessionId, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        if (result.DecodedBody is not null) {
            logger.WriteLine($"auth.session_start.response.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
            AutomatedActionTraceLogging.LogTrace("auth.session_start.trace", result.DecodedBody, logger);
            LogSessionStartDetails(result.DecodedBody, logger);
        }

        logger.WriteLine($"auth.session_start.result_status={result.StatusCode}");
        logger.WriteLine($"auth.session_start.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }

    private static void LogSessionStartDetails(Dictionary<string, object?> responseBody, SessionLogger logger) {
        var details = SessionStartResponseParser.Parse(responseBody);
        if (details is null) {
            return;
        }
        SessionStartExecutionSupport.LogParsedDetails("auth.session_start", details, logger);
        if (!string.IsNullOrWhiteSpace(details.FinalEvent)) {
            logger.WriteLine($"auth.session_start.call_home_endpoint={details.FinalEvent}");
        }
    }
}
