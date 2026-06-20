using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionPlan : IAction {
    public Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        var runtime = SessionRuntime.Create(config);
        var transport = new NullTransport();

        HeadsetSupport.PrintBanner(config, runtime, logger);
        logger.WriteLine("offline bootstrap plan:");
        logger.WriteLine($"  1. connect RFCOMM/SPP to {config.DeviceAddress} using {BluetoothConstants.SerialPortServiceClassUuid}");
        logger.WriteLine($"  2. send {ProtocolSupport.DescribeFrame(GaiaPackets.BuildCommand(GaiaCommandId.CryptoAppGenerateChallenge))}");
        logger.WriteLine("  3. receive 16-byte headset app challenge");
        logger.WriteLine("  4. compute GMAC over that challenge with the app session crypto");
        logger.WriteLine("  5. send CryptoAppValidateChallengeResponse with nonce||gmac");
        logger.WriteLine("  6. receive headset validate-response GMAC");
        logger.WriteLine("  7. verify it against \"Kyle is awesome!\"");
        logger.WriteLine("  8. if valid, start safe authenticated reads");
        logger.WriteLine();

        logger.WriteLine("safe authenticated reads:");
        foreach (var query in NuraQueries.SafeStartupReads(config.CurrentProfileId)) {
            var frame = GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, query.Payload);
            logger.WriteLine($"  {query.Description}: {ProtocolSupport.DescribeFrame(frame)}");
        }

        logger.WriteLine();
        logger.WriteLine($"transport stub: {transport.Describe()}");
        return Task.FromResult(0);
    }
}
