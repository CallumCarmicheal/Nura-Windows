using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionRespond : IAction {
    public Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        var challengeHex = ArgumentReader.RequiredValue(args, "--challenge-hex");
        var runtime = SessionRuntime.Create(config);
        var challenge = Hex.Parse(challengeHex);
        if (challenge.Length != 16) {
            throw new InvalidOperationException("challenge must be exactly 16 bytes");
        }

        var gmac = runtime.Crypto.GenerateChallengeResponse(challenge);
        var validateFrame = GaiaPackets.BuildCommand(
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            ByteArray.Combine(runtime.Nonce, gmac));

        HeadsetSupport.PrintBanner(config, runtime, logger);
        logger.WriteLine($"challenge.hex={Hex.Format(challenge)}");
        logger.WriteLine($"response.gmac.hex={Hex.Format(gmac)}");
        logger.WriteLine($"validate.frame.hex={Hex.Format(validateFrame.Bytes)}");
        return Task.FromResult(0);
    }
}
