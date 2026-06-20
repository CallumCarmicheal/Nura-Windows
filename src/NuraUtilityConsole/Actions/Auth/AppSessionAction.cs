using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthAppSession : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);

        var endpoint = ArgumentReader.OptionalValue(args, "--endpoint") ?? "app/session";
        var appStartTimeUnixMilliseconds = AuthStateSupport.ParseOptionalInt64(args, "--app-start-time")
            ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.app_session.authenticated=False");
        logger.WriteLine($"auth.app_session.endpoint={endpoint}");
        logger.WriteLine($"auth.app_session.mode=app_start_cold");
        logger.WriteLine($"auth.app_session.app_start_time_unix_ms={appStartTimeUnixMilliseconds}");
        logger.WriteLine($"auth.app_session.uuid={state.Uuid}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.AppSessionAsync(state, endpoint, appStartTimeUnixMilliseconds, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.app_session.result_status={result.StatusCode}");
        logger.WriteLine($"auth.app_session.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }
}
