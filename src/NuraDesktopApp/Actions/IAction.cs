using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal interface IAction {
    Task<int> HandleAsync(string[] args, SessionLogger logger);
}
