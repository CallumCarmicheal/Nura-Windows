using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthShowState : IAction {
    public Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={state.EmailAddress ?? string.Empty}");
        logger.WriteLine($"auth.has_authenticated_session={state.HasAuthenticatedSession}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.access_token={state.AccessToken ?? string.Empty}");
        logger.WriteLine($"auth.client_key={state.ClientKey ?? string.Empty}");
        AuthStateSupport.LogSessionState(state, logger);
        if (state.TokenExpiryUnixSeconds is { } expiryUnixSeconds) {
            logger.WriteLine($"auth.token_expiry_unix={expiryUnixSeconds}");
            logger.WriteLine($"auth.token_expiry_utc={DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds):O}");
        }

        return Task.FromResult(0);
    }
}
