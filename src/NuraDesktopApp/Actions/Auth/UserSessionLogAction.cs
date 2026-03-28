using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthUserSessionLog : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        var endpoint = ArgumentReader.OptionalValue(args, "--endpoint") ?? "user_session/log";
        var userSessionId = AuthStateSupport.ParseOptionalInt32(args, "--usid");
        IReadOnlyDictionary<string, object?>? payload = null;
        if (userSessionId is not null) {
            payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["usid"] = userSessionId.Value
            };
        }

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.user_session.endpoint={endpoint}");
        if (userSessionId is not null) {
            logger.WriteLine($"auth.user_session.usid={userSessionId.Value}");
        }
        AuthStateSupport.LogSessionState(state, logger);

        using var client = new NuraAuthApiClient(logger);
        var result = await client.CallAuthenticatedEndpointAsync(state, endpoint, payload, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.user_session.result_status={result.StatusCode}");
        logger.WriteLine($"auth.user_session.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }
}
