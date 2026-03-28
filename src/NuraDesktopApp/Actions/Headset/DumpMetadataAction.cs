using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionDumpMetadata : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var config = LocalStateFiles.LoadConfig(logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runtime = SessionRuntime.CreateWithFreshNonce(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        HeadsetSupport.PrintBanner(config, runtime, logger);
        logger.WriteLine("session.nonce_mode=fresh_random");
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cts.Token);
        logger.WriteLine("connected");

        await HeadsetSupport.PerformAppHandshakeAsync(runtime, transport, logger, cts.Token);

        var deepSleepPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse("006c"), "metadata.deep_sleep", cts.Token);
        logger.WriteLine($"metadata.deep_sleep.seconds={HeadsetSupport.DecodeUInt16BigEndian(deepSleepPlain)}");

        var currentProfilePlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse("0041"), "metadata.current_profile", cts.Token);
        var currentProfileId = currentProfilePlain.Length > 0 ? currentProfilePlain[0] : 0;
        logger.WriteLine($"metadata.current_profile.id={currentProfileId}");

        for (var profileId = 0; profileId < 3; profileId++) {
            var namePlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse($"001a{profileId:x2}"), $"metadata.profile_name_{profileId}", cts.Token);
            logger.WriteLine($"metadata.profile.{profileId}.name={HeadsetSupport.DecodeProfileName(namePlain)}");

            var ancPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, HeadsetSupport.BuildGetAncStatePayload(profileId), $"metadata.anc_state_{profileId}", cts.Token);
            if (ancPlain.Length >= 2) {
                var ancState = new AncState(ancPlain[0], ancPlain[1]);
                logger.WriteLine($"metadata.profile.{profileId}.anc.primary_raw={ancState.PrimaryRaw}");
                logger.WriteLine($"metadata.profile.{profileId}.anc.secondary_raw={ancState.SecondaryRaw}");
                logger.WriteLine($"metadata.profile.{profileId}.anc.mode={HeadsetSupport.DescribeAncMode(ancState)}");
            }
        }

        var genericModePlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse("0042"), "metadata.generic_mode", cts.Token);
        logger.WriteLine($"metadata.generic_mode.enabled={HeadsetSupport.DecodeBoolean(genericModePlain)}");

        var kickitParamsPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse($"004d{currentProfileId:x2}"), "metadata.kickit_params", cts.Token);
        logger.WriteLine($"metadata.kickit.current_profile.params.hex={Hex.Format(kickitParamsPlain)}");
        if (HeadsetSupport.TryDecodeKickitParams(kickitParamsPlain, out var kickitParams)) {
            logger.WriteLine($"metadata.kickit.current_profile.drc={kickitParams.Drc}");
            logger.WriteLine($"metadata.kickit.current_profile.lpf={kickitParams.Lpf}");
            logger.WriteLine($"metadata.kickit.current_profile.gain={kickitParams.Gain}");
        }

        var kickitEnabledPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse("00b4"), "metadata.kickit_enabled", cts.Token);
        logger.WriteLine($"metadata.kickit.current_profile.enabled={HeadsetSupport.DecodeBoolean(kickitEnabledPlain)}");

        var batteryPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse("007f"), "metadata.battery", cts.Token);
        logger.WriteLine($"metadata.battery.payload.hex={Hex.Format(batteryPlain)}");
        if (HeadsetSupport.TryDecodeBatteryStatus(batteryPlain, out var batteryStatus)) {
            HeadsetSupport.LogBatteryStatus(logger, "metadata.battery", batteryStatus);
        }

        var euAttenuationPlain = await HeadsetSupport.ReadAuthenticatedPayloadAsync(runtime, transport, logger, Hex.Parse("0087"), "metadata.eu_attenuation", cts.Token);
        logger.WriteLine($"metadata.eu_attenuation.raw={Hex.Format(euAttenuationPlain)}");
        logger.WriteLine($"metadata.eu_attenuation.enabled={HeadsetSupport.DecodeBoolean(euAttenuationPlain)}");
        logger.WriteLine("metadata.result=success");
        return 0;
    }
}
