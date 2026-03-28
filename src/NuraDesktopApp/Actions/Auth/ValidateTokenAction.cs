using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthValidateToken : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        var withAppContext = args.Any(arg => string.Equals(arg, "--with-app-context", StringComparison.OrdinalIgnoreCase));
        var appStartTime = AuthStateSupport.ParseOptionalInt64(args, "--app-start-time");
        logger.WriteLine($"auth.validate_token.with_app_context={withAppContext}");
        if (appStartTime is not null) {
            logger.WriteLine($"auth.validate_token.app_start_time={appStartTime}");
        }

        using var client = new NuraAuthApiClient(logger);
        var result = await client.ValidateTokenAsync(state, withAppContext, appStartTime, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.validate_token.result_status={result.StatusCode}");
        logger.WriteLine($"auth.has_authenticated_session={updatedState.HasAuthenticatedSession}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }
}
