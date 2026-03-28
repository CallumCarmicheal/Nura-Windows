using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAncToggleTest : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        var authState = LocalStateFiles.LoadAuthState(logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runtime = TryCreateRuntimeFromAuth(authState, logger) ?? SessionRuntime.Create(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        PrintBanner(config, authState, runtime, logger);
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cts.Token);
        logger.WriteLine("connected");

        await HeadsetSupport.PerformAppHandshakeAsync(runtime, transport, logger, cts.Token);

        var currentProfile = await HeadsetSupport.ReadCurrentProfileAsync(runtime, transport, logger, cts.Token);
        logger.WriteLine($"anc_test.current_profile={currentProfile}");

        var originalState = await HeadsetSupport.ReadAncStateAsync(runtime, transport, logger, currentProfile, "before", cts.Token);
        HeadsetSupport.LogAncState(logger, "anc_test.before", originalState);

        var toggledState = new AncState(
            originalState.PrimaryRaw,
            originalState.SecondaryRaw == 0 ? (byte)0x01 : (byte)0x00);
        HeadsetSupport.LogAncState(logger, "anc_test.toggle.target", toggledState);
        await HeadsetSupport.SetAncStateAsync(runtime, transport, logger, currentProfile, toggledState, "toggle", cts.Token);

        var afterToggleState = await HeadsetSupport.ReadAncStateAsync(runtime, transport, logger, currentProfile, "after_toggle", cts.Token);
        HeadsetSupport.LogAncState(logger, "anc_test.after_toggle", afterToggleState);

        logger.WriteLine("anc_test.waiting_ms=5000");
        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);

        await HeadsetSupport.SetAncStateAsync(runtime, transport, logger, currentProfile, originalState, "restore", cts.Token);
        var restoredState = await HeadsetSupport.ReadAncStateAsync(runtime, transport, logger, currentProfile, "restored", cts.Token);
        HeadsetSupport.LogAncState(logger, "anc_test.restored", restoredState);
        return 0;
    }

    private static SessionRuntime? TryCreateRuntimeFromAuth(NuraAuthState authState, SessionLogger logger) {
        if (string.IsNullOrWhiteSpace(authState.AppEncKey) || string.IsNullOrWhiteSpace(authState.AppEncNonce)) {
            logger.WriteLine("anc_test.runtime_source=config.device_key");
            return null;
        }

        try {
            var key = Convert.FromBase64String(authState.AppEncKey);
            var nonce = Convert.FromBase64String(authState.AppEncNonce);
            logger.WriteLine("anc_test.runtime_source=auth.app_enc");
            return SessionRuntime.Create(key, nonce);
        } catch (FormatException ex) {
            logger.WriteLine($"anc_test.runtime_source_error={ex.Message}");
            logger.WriteLine("anc_test.runtime_source=config.device_key");
            return null;
        }
    }

    private static void PrintBanner(
        Config.NuraOfflineConfig config,
        NuraAuthState authState,
        SessionRuntime runtime,
        SessionLogger logger) {
        logger.WriteLine($"device.address={config.DeviceAddress}");
        logger.WriteLine($"device.serial={config.SerialNumber}");
        logger.WriteLine($"device.profile={config.CurrentProfileId}");
        if (!string.IsNullOrWhiteSpace(authState.AppEncKey)) {
            try {
                logger.WriteLine($"device.key.hex={Hex.Format(Convert.FromBase64String(authState.AppEncKey))}");
            } catch (FormatException) {
                logger.WriteLine($"device.key.hex={Hex.Format(config.DeviceKey)}");
            }
        } else {
            logger.WriteLine($"device.key.hex={Hex.Format(config.DeviceKey)}");
        }

        logger.WriteLine($"session.nonce.hex={Hex.Format(runtime.Nonce)}");
        logger.WriteLine($"session.enc_counter={runtime.Crypto.EncryptCounter}");
        logger.WriteLine($"session.dec_counter={runtime.Crypto.DecryptCounter}");
        logger.WriteLine();
    }
}
