using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionEncrypt : IAction {
    public Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        var payloadHex = ArgumentReader.RequiredValue(args, "--payload-hex");
        var runtime = SessionRuntime.Create(config);
        var payload = Hex.Parse(payloadHex);
        var authenticate = !ArgumentReader.HasFlag(args, "--unauth");
        var frame = authenticate
            ? GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, payload)
            : GaiaPackets.BuildUnauthenticatedAppCommand(runtime.Crypto, payload);

        HeadsetSupport.PrintBanner(config, runtime, logger);
        logger.WriteLine($"plain.hex={Hex.Format(payload)}");
        logger.WriteLine($"authenticate={authenticate}");
        logger.WriteLine($"frame.hex={Hex.Format(frame.Bytes)}");
        return Task.FromResult(0);
    }
}
