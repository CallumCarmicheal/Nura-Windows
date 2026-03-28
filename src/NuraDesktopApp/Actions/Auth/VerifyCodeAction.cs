using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthVerifyCode : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        var emailAddress = ArgumentReader.OptionalValue(args, "--email") ?? state.EmailAddress
            ?? throw new InvalidOperationException("email is required for verify-code when auth state does not already contain one");
        var oneTimeCode = ArgumentReader.RequiredValue(args, "--code");

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={emailAddress}");
        logger.WriteLine($"auth.code_length={oneTimeCode.Length}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.VerifyCodeAsync(state, emailAddress, oneTimeCode, cts.Token);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, emailAddress, result.AuthUid ?? emailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.has_authenticated_session={updatedState.HasAuthenticatedSession}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        if (updatedState.TokenExpiryUnixSeconds is { } expiryUnixSeconds) {
            logger.WriteLine($"auth.token_expiry_unix={expiryUnixSeconds}");
            logger.WriteLine($"auth.token_expiry_utc={DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds):O}");
        }

        return result.IsSuccessStatusCode ? 0 : 1;
    }
}
