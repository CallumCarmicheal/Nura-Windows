using NuraLib.Crypto;
using NuraLib.Devices;

namespace NuraLib.Protocol;

internal static class NuraCommandFactory {
    public static GetDeviceInfoCommand CreateGetDeviceInfo() => new();

    public static GetExtendedDeviceInfoCommand CreateGetExtendedDeviceInfo() => new();

    public static GenerateAppChallengeCommand CreateGenerateAppChallenge() => new();

    public static ValidateAppChallengeResponseCommand CreateValidateAppChallenge(NuraSessionRuntime runtime, byte[] gmac) =>
        new(runtime, gmac);

    public static GetCurrentProfileIdCommand CreateGetCurrentProfileId() => new();

    public static GetProfileNameCommand CreateGetProfileName(int profileId) => new(profileId);

    public static SelectProfileCommand CreateSelectProfile(int profileId) => new(profileId);

    public static GetAncStateCommand CreateGetAncState(int profileId) => new(profileId);

    public static SetAncStateCommand CreateSetAncState(int profileId, NuraAncState state) => new(profileId, state);

    public static SetTemporaryAncStateCommand CreateSetTemporaryAncState(bool ancEnabled, bool passthroughEnabled) =>
        new(ancEnabled, passthroughEnabled);

    public static GetAncLevelCommand CreateGetAncLevel(int profileId) => new(profileId);

    public static SetAncLevelCommand CreateSetAncLevel(int profileId, int level) => new(profileId, level);

    public static GetGlobalAncEnabledCommand CreateGetGlobalAncEnabled(int profileId) => new(profileId);

    public static SetGlobalAncEnabledCommand CreateSetGlobalAncEnabled(int profileId, bool enabled) => new(profileId, enabled);

    public static GetKickitEnabledCommand CreateGetKickitEnabled() => new();

    public static SetKickitEnabledCommand CreateSetKickitEnabled(bool enabled) => new(enabled);

    public static GetKickitStateCommand CreateGetKickitState(int profileId) => new(profileId);

    public static SetKickitStateCommand CreateSetKickitState(int profileId, int? levelRaw, bool? enabled) =>
        new(profileId, levelRaw, enabled);

    public static GetSpatialStateCommand CreateGetSpatialState() => new();

    public static SetSpatialStateCommand CreateSetSpatialState(bool enabled) => new(enabled);

    public static GetButtonConfigurationCommand CreateGetButtonConfiguration(NuraDeviceInfo deviceInfo, int profileId) =>
        new(deviceInfo, profileId);

    public static SetButtonConfigurationCommand CreateSetButtonConfiguration(NuraDeviceInfo deviceInfo, int profileId, NuraButtonConfiguration configuration) =>
        new(deviceInfo, profileId, configuration);

    public static GetDialConfigurationCommand CreateGetDialConfiguration(int profileId) => new(profileId);

    public static SetDialConfigurationCommand CreateSetDialConfiguration(int profileId, NuraDialConfiguration configuration) =>
        new(profileId, configuration);

    public static SetVoicePromptGainCommand CreateSetVoicePromptGain(NuraVoicePromptGain gain) => new(gain);
}
