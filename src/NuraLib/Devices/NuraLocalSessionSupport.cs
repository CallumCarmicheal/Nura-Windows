using NuraLib.Crypto;
using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Transport;
using NuraLib.Utilities;

namespace NuraLib.Devices;

internal static class NuraLocalSessionSupport {
    private const string Source = nameof(NuraLocalSessionSupport);

    public static async Task PerformAppHandshakeAsync(
        NuraSessionRuntime runtime,
        IHeadsetTransport transport,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        var challenge = await transport.ExecuteAsync(
            NuraCommandFactory.CreateGenerateAppChallenge(),
            runtime,
            cancellationToken);

        if (challenge.Length != 16) {
            throw new InvalidOperationException($"unexpected challenge length: {challenge.Length}");
        }

        logger.Trace(Source, $"challenge.hex={HexEncoding.Format(challenge)}");
        var gmac = runtime.Crypto.GenerateChallengeResponse(challenge);
        logger.Trace(Source, $"response.gmac.hex={HexEncoding.Format(gmac)}");

        var headsetGmac = await transport.ExecuteAsync(
            NuraCommandFactory.CreateValidateAppChallenge(runtime, gmac),
            runtime,
            cancellationToken);

        logger.Trace(Source, $"headset.gmac.hex={HexEncoding.Format(headsetGmac)}");
        var success = runtime.Crypto.ValidateResponse(headsetGmac);
        logger.Information(Source, $"handshake.success={success}");
        if (!success) {
            logger.Warning(Source, "Headset GMAC did not match local expectation; continuing because the headset accepted our challenge response.");
        }
    }

    public static async Task<int> ReadCurrentProfileAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetCurrentProfileId();
        var profileId = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"current_profile.command={command.Name}");
        return profileId;
    }

    public static async Task<string> ReadProfileNameAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetProfileName(profileId);
        var name = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"profile_name.command={command.Name}");
        return name;
    }

    public static async Task SelectProfileAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSelectProfile(profileId);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"profile_select.command={command.Name}");
        logger.Trace(Source, $"profile_select.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<NuraAncState> ReadAncStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetAncState(profileId);
        var state = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"anc_state.command={command.Name}");
        return state;
    }

    public static async Task SetAncStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        NuraAncState state,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetAncState(profileId, state);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"anc_set.command={command.Name}");
        logger.Trace(Source, $"anc_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task SetTemporaryAncStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        bool ancEnabled,
        bool passthroughEnabled,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetTemporaryAncState(ancEnabled, passthroughEnabled);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"anc_temporary_set.command={command.Name}");
        logger.Trace(Source, $"anc_temporary_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<int> ReadAncLevelAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetAncLevel(profileId);
        var level = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"anc_level.command={command.Name}");
        return level;
    }

    public static async Task SetAncLevelAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        int level,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetAncLevel(profileId, level);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"anc_level_set.command={command.Name}");
        logger.Trace(Source, $"anc_level_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<bool> ReadGlobalAncEnabledAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetGlobalAncEnabled(profileId);
        var enabled = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"global_anc.command={command.Name}");
        return enabled;
    }

    public static async Task SetGlobalAncEnabledAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        bool enabled,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetGlobalAncEnabled(profileId, enabled);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"global_anc_set.command={command.Name}");
        logger.Trace(Source, $"global_anc_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<NuraPersonalisationMode> ReadKickitEnabledAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetKickitEnabled();
        var mode = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"kickit_enabled.command={command.Name}");
        return mode;
    }

    public static async Task SetKickitEnabledAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        NuraPersonalisationMode mode,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetKickitEnabled(mode == NuraPersonalisationMode.Personalised);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"kickit_enabled_set.command={command.Name}");
        logger.Trace(Source, $"kickit_enabled_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<NuraKickitState> ReadKickitStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetKickitState(profileId);
        var state = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"kickit_state.command={command.Name}");
        return state;
    }

    public static async Task SetKickitStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        int? levelRaw,
        bool? enabled,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetKickitState(profileId, levelRaw, enabled);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"kickit_state_set.command={command.Name}");
        logger.Trace(Source, $"kickit_state_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<bool> ReadSpatialEnabledAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetSpatialState();
        var enabled = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"spatial.command={command.Name}");
        return enabled;
    }

    public static async Task SetSpatialEnabledAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        bool enabled,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetSpatialState(enabled);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"spatial_set.command={command.Name}");
        logger.Trace(Source, $"spatial_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<NuraButtonConfiguration> ReadButtonConfigurationAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        NuraDeviceInfo deviceInfo,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetButtonConfiguration(deviceInfo, profileId);
        var configuration = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"button_configuration.command={command.Name}");
        return configuration;
    }

    public static async Task SetButtonConfigurationAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        NuraDeviceInfo deviceInfo,
        int profileId,
        NuraButtonConfiguration configuration,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetButtonConfiguration(deviceInfo, profileId, configuration);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"button_configuration_set.command={command.Name}");
        logger.Trace(Source, $"button_configuration_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task<NuraDialConfiguration> ReadDialConfigurationAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateGetDialConfiguration(profileId);
        var configuration = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"dial_configuration.command={command.Name}");
        return configuration;
    }

    public static async Task SetDialConfigurationAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        NuraDialConfiguration configuration,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetDialConfiguration(profileId, configuration);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"dial_configuration_set.command={command.Name}");
        logger.Trace(Source, $"dial_configuration_set.ack.hex={HexEncoding.Format(ack)}");
    }

    public static async Task SetVoicePromptGainAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        NuraVoicePromptGain gain,
        CancellationToken cancellationToken) {
        var command = NuraCommandFactory.CreateSetVoicePromptGain(gain);
        var ack = await session.ExecuteAsync(command, cancellationToken);
        logger.Trace(Source, $"voice_prompt_gain_set.command={command.Name}");
        logger.Trace(Source, $"voice_prompt_gain_set.ack.hex={HexEncoding.Format(ack)}");
    }
}
