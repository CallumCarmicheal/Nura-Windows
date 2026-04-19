using NuraLib.Crypto;
using NuraLib.Devices;

namespace NuraLib.Protocol;

internal static class NuraQueryFactory {
    public static NuraQuery CreateGetDeepSleepTimeoutQuery() =>
        new(NuraQueryId.GetDeepSleepTimeout, "GetDeepSleepTimeout", CreateGetDeepSleepTimeoutPayload());

    public static NuraQuery CreateGetCurrentProfileIdQuery() =>
        new(NuraQueryId.GetCurrentProfileId, "GetCurrentProfileID", CreateGetCurrentProfileIdPayload());

    public static NuraQuery CreateGetProfileNameQuery(int profileId) =>
        new(NuraQueryId.GetProfileName, $"GetProfileName(profile={profileId})", CreateGetProfileNamePayload(profileId));

    public static NuraQuery CreateGetGenericModeEnabledQuery() =>
        new(NuraQueryId.GetGenericModeEnabled, "IsGenericModeEnabled", CreateGetGenericModeEnabledPayload());

    public static NuraQuery CreateGetAncStateQuery(int profileId) =>
        new(NuraQueryId.GetAncState, $"GetANCState(profile={profileId})", CreateGetAncStatePayload(profileId));

    public static NuraQuery CreateSetAncStateQuery(int profileId, NuraAncState state) =>
        new(NuraQueryId.SetAncState, $"SetANCState(profile={profileId})", CreateSetAncStatePayload(profileId, state));

    public static NuraQuery CreateGetKickitParamsQuery(int profileId) =>
        new(NuraQueryId.GetKickitParams, $"GetKickitParams(profile={profileId})", CreateGetKickitParamsPayload(profileId));

    public static NuraQuery CreateReadBatteryQuery() =>
        new(NuraQueryId.ReadBattery, "ReadBattery", CreateReadBatteryPayload());

    public static NuraQuery CreateGetEuAttenuationQuery() =>
        new(NuraQueryId.GetEuAttenuation, "GetEUAttenuation", CreateGetEuAttenuationPayload());

    public static NuraQuery CreateGetKickitEnabledQuery(int profileId) =>
        new(NuraQueryId.GetKickitEnabled, $"GetKickitEnabled(profile={profileId})", CreateGetKickitEnabledPayload(profileId));

    public static byte[] CreateGetDeepSleepTimeoutPayload() => [0x00, 0x6c];

    public static byte[] CreateGetCurrentProfileIdPayload() => [0x00, 0x41];

    public static byte[] CreateGetProfileNamePayload(int profileId) => [0x00, 0x1a, checked((byte)profileId)];

    public static byte[] CreateGetGenericModeEnabledPayload() => [0x00, 0x42];

    public static byte[] CreateGetAncStatePayload(int profileId) => [0x00, 0x49, checked((byte)profileId)];

    public static byte[] CreateSetAncStatePayload(int profileId, NuraAncState state) {
        var primaryRaw = state.AncEnabled ? (byte)0x01 : (byte)0x00;
        var secondaryRaw = state.PassthroughEnabled ? (byte)0x01 : (byte)0x00;

        return [0x00, 0x48, checked((byte)profileId), primaryRaw, secondaryRaw];
    }

    public static byte[] CreateGetKickitParamsPayload(int profileId) => [0x00, 0x4d, checked((byte)profileId)];

    public static byte[] CreateReadBatteryPayload() => [0x00, 0x7f];

    public static byte[] CreateGetEuAttenuationPayload() => [0x00, 0x87];

    public static byte[] CreateGetKickitEnabledPayload(int profileId) => [0x00, 0xb4];

    public static GaiaFrame CreateGetDeviceInfo() =>
        GaiaPacketFactory.CreateCommand(GaiaCommandId.GetDeviceInfo);

    public static GaiaFrame CreateGetExtendedDeviceInfo() =>
        GaiaPacketFactory.CreateCommand(GaiaCommandId.GetExtendedDeviceInfo);

    public static GaiaFrame CreateGenerateAppChallenge() =>
        GaiaPacketFactory.CreateCommand(GaiaCommandId.CryptoAppGenerateChallenge);

    public static GaiaFrame CreateValidateAppChallenge(NuraSessionRuntime runtime, byte[] gmac) =>
        GaiaPacketFactory.CreateCommand(
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            [.. runtime.Nonce, .. gmac]);

    public static GaiaFrame CreateGetCurrentProfileId(NuraSessionRuntime runtime) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetCurrentProfileIdPayload());

    public static GaiaFrame CreateGetAncState(NuraSessionRuntime runtime, int profileId) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetAncStatePayload(profileId));

    public static GaiaFrame CreateSetAncState(NuraSessionRuntime runtime, int profileId, NuraAncState state) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateSetAncStatePayload(profileId, state));

    public static GaiaFrame CreateReadBattery(NuraSessionRuntime runtime) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateReadBatteryPayload());

    public static GaiaFrame CreateGetProfileName(NuraSessionRuntime runtime, int profileId) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetProfileNamePayload(profileId));

    public static GaiaFrame CreateGetDeepSleepTimeout(NuraSessionRuntime runtime) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetDeepSleepTimeoutPayload());

    public static GaiaFrame CreateGetGenericModeEnabled(NuraSessionRuntime runtime) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetGenericModeEnabledPayload());

    public static GaiaFrame CreateGetKickitParams(NuraSessionRuntime runtime, int profileId) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetKickitParamsPayload(profileId));

    public static GaiaFrame CreateGetEuAttenuation(NuraSessionRuntime runtime) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetEuAttenuationPayload());

    public static GaiaFrame CreateGetKickitEnabled(NuraSessionRuntime runtime, int profileId) =>
        GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreateGetKickitEnabledPayload(profileId));

    public static IReadOnlyList<NuraQuery> CreateSafeStartupReads(int currentProfileId) {
        return
        [
            CreateGetDeepSleepTimeoutQuery(),
            CreateGetCurrentProfileIdQuery(),
            CreateGetProfileNameQuery(0),
            CreateGetProfileNameQuery(1),
            CreateGetProfileNameQuery(2),
            CreateGetGenericModeEnabledQuery(),
            CreateGetAncStateQuery(0),
            CreateGetAncStateQuery(1),
            CreateGetAncStateQuery(2),
            CreateGetKickitParamsQuery(currentProfileId),
            CreateReadBatteryQuery(),
            CreateGetEuAttenuationQuery(),
            CreateGetKickitEnabledQuery(currentProfileId)
        ];
    }
}
