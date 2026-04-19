using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class GetButtonConfigurationCommand : NuraAppEncryptedCommand<NuraButtonConfiguration> {
    public GetButtonConfigurationCommand(NuraDeviceInfo deviceInfo, int profileId) {
        DeviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        ProfileId = profileId;
    }

    public NuraDeviceInfo DeviceInfo { get; }

    public int ProfileId { get; }

    public override string Name => $"GetButtonConfiguration(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [
        0x00,
        GetCommandIdLowByte(),
        checked((byte)ProfileId)
    ];

    protected override NuraButtonConfiguration ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeButtonConfiguration(
            plainPayload,
            supportsDoubleTap: DeviceInfo.SupportsButtonGesture(NuraButtonGesture.DoubleTap),
            supportsTripleTap: DeviceInfo.SupportsButtonGesture(NuraButtonGesture.TripleTap));

    private byte GetCommandIdLowByte() {
        if (DeviceInfo.SupportsButtonGesture(NuraButtonGesture.TripleTap)) {
            return 0x73;
        }

        return DeviceInfo.FirmwareVersion > 510 ? (byte)0xB7 : (byte)0x51;
    }
}
