using System.Text;

using NuraDesktopConsole.Actions;
using NuraDesktopConsole.Library;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole;

internal static class Program {
    private static async Task<int> Main(string[] args) {
        Console.OutputEncoding = Encoding.UTF8;
        using var logger = SessionLogger.CreateDefault();

        try {
            if (IsHelpRequest(args)) {
                ActionHandler.PrintHelp();
                return 0;
            }

            var commandName = ActionHandler.ResolveCommandName(args);
            logger.WriteLine($"command.name={commandName}");
            logger.WriteLine($"log.path={logger.LogPath}");

            var action = ActionHandler.Find(commandName);
            if (action is null) {
                logger.Error($"unknown command: {commandName}");
                ActionHandler.PrintHelp();
                return 1;
            }

            return await action.Factory().HandleAsync(args, logger);
        } catch (Exception ex) {
            logger.Error($"fatal: {ex}");
            return 1;
        }
    }

    private static bool IsHelpRequest(string[] args) {
        if (args.Length == 0) {
            return true;
        }

        return args[0] is "-h" or "--help" or "help" or "?" or "h"
            || string.Equals(ArgumentReader.OptionalValue(args, "--help"), "--help", StringComparison.OrdinalIgnoreCase);
    }
}
