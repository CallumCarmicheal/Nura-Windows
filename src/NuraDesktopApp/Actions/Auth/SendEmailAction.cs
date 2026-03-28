using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSendEmail : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var emailAddress = ArgumentReader.RequiredValue(args, "--email");
        var state = NuraAuthState.LoadOrCreate(authPath) with { EmailAddress = emailAddress };

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={emailAddress}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.SendLoginEmailAsync(state, emailAddress, cts.Token);
        AuthStateSupport.ApplyAuthResultToState(state, result, emailAddress).Save(authPath);

        logger.WriteLine($"auth.result=send_email_complete status={result.StatusCode}");
        return result.IsSuccessStatusCode ? 0 : 1;
    }
}
