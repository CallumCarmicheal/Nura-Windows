using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionFreshNonceTest : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var runtime = SessionRuntime.CreateWithFreshNonce(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        HeadsetSupport.PrintBanner(config, runtime, logger);
        logger.WriteLine("session.nonce_mode=fresh_random");
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
            "fresh_nonce_deep_sleep",
            cts.Token);
        logger.WriteLine($"fresh_nonce.deep_sleep.payload.hex={Hex.Format(deepSleepPlain)}");

        var currentProfileId = await HeadsetSupport.ReadCurrentProfileAsync(runtime, transport, logger, cts.Token);
        logger.WriteLine($"fresh_nonce.current_profile={currentProfileId}");

        var batteryPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(
            runtime,
            transport,
            logger,
            Hex.Parse("007f"),
            "fresh_nonce_battery",
            cts.Token);
        logger.WriteLine($"fresh_nonce.battery.payload.hex={Hex.Format(batteryPlain)}");
        if (HeadsetSupport.TryDecodeBatteryStatus(batteryPlain, out var batteryStatus)) {
            logger.WriteLine($"fresh_nonce.battery.voltage_mv={batteryStatus.BatteryVoltageMillivolts}");
            logger.WriteLine($"fresh_nonce.battery.level_raw={batteryStatus.BatteryLevelRaw}");
            logger.WriteLine($"fresh_nonce.battery.percent={batteryStatus.BatteryPercentage}");
        }

        logger.WriteLine("fresh_nonce.result=success");
        return 0;
    }
}
