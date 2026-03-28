using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSessionLog : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        var endpoint = ArgumentReader.OptionalValue(args, "--endpoint") ?? "session/log";
        var sessionId = AuthStateSupport.ParseOptionalInt32(args, "--session");
        IReadOnlyDictionary<string, object?>? payload = null;
        if (sessionId is not null) {
            payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["session"] = sessionId.Value
            };
        }

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.session_log.endpoint={endpoint}");
        if (sessionId is not null) {
            logger.WriteLine($"auth.session_log.session={sessionId.Value}");
        }
        AuthStateSupport.LogSessionState(state, logger);

        using var client = new NuraAuthApiClient(logger);
        var result = await client.CallAuthenticatedEndpointAsync(state, endpoint, payload, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.session_log.result_status={result.StatusCode}");
        logger.WriteLine($"auth.session_log.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }
}
