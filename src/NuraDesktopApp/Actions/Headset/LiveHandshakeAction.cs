using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionLiveHandshake : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var runtime = SessionRuntime.Create(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        HeadsetSupport.PrintBanner(config, runtime, logger);
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cts.Token);
        logger.WriteLine("connected");

        await HeadsetSupport.PerformAppHandshakeAsync(runtime, transport, logger, cts.Token);

        var deepSleepPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(
            runtime,
            transport,
            logger,
            Hex.Parse("006c"),
            "deep_sleep",
            cts.Token);
        logger.WriteLine($"post_auth.deep_sleep.payload.hex={Hex.Format(deepSleepPlain)}");

        var currentProfileId = await HeadsetSupport.ReadCurrentProfileAsync(runtime, transport, logger, cts.Token);
        logger.WriteLine($"post_auth.current_profile={currentProfileId}");

        var batteryPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(
            runtime,
            transport,
            logger,
            Hex.Parse("007f"),
            "battery",
            cts.Token);
        logger.WriteLine($"post_auth.battery.payload.hex={Hex.Format(batteryPlain)}");
        if (HeadsetSupport.TryDecodeBatteryStatus(batteryPlain, out var batteryStatus)) {
            HeadsetSupport.LogBatteryStatus(logger, "post_auth.battery", batteryStatus);
        }

        return 0;
    }
}
