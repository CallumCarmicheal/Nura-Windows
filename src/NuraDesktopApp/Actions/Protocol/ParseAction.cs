using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionParse : IAction {
    public Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var frameHex = ArgumentReader.RequiredValue(args, "--frame-hex");
        var response = GaiaResponse.Parse(Hex.Parse(frameHex));
        logger.WriteLine(response.ToDisplayString());
        return Task.FromResult(0);
    }
}
