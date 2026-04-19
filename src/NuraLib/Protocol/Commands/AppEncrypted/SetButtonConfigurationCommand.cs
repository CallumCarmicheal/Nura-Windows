using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class SetButtonConfigurationCommand : NuraAppEncryptedCommand<byte[]> {
    public SetButtonConfigurationCommand(NuraDeviceInfo deviceInfo, int profileId, NuraButtonConfiguration configuration) {
        DeviceInfo = deviceInfo ?? throw new ArgumentNullException(nameof(deviceInfo));
        ProfileId = profileId;
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public NuraDeviceInfo DeviceInfo { get; }

    public int ProfileId { get; }

    public NuraButtonConfiguration Configuration { get; }

    public override string Name => $"SetButtonConfiguration(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() {
        var payload = new List<byte> {
            0x00,
            GetCommandIdLowByte(),
            checked((byte)ProfileId),
            SerializeRequiredBinding(NuraButtonSide.Left, NuraButtonGesture.SingleTap),
            SerializeRequiredBinding(NuraButtonSide.Right, NuraButtonGesture.SingleTap)
        };

        if (DeviceInfo.SupportsButtonGesture(NuraButtonGesture.DoubleTap)) {
            payload.Add(SerializeRequiredBinding(NuraButtonSide.Left, NuraButtonGesture.DoubleTap));
            payload.Add(SerializeRequiredBinding(NuraButtonSide.Right, NuraButtonGesture.DoubleTap));
            payload.Add(SerializeRequiredBinding(NuraButtonSide.Left, NuraButtonGesture.TapAndHold));
            payload.Add(SerializeRequiredBinding(NuraButtonSide.Right, NuraButtonGesture.TapAndHold));
        }

        if (DeviceInfo.SupportsButtonGesture(NuraButtonGesture.TripleTap)) {
            payload.Add(SerializeRequiredBinding(NuraButtonSide.Left, NuraButtonGesture.TripleTap));
            payload.Add(SerializeRequiredBinding(NuraButtonSide.Right, NuraButtonGesture.TripleTap));
        }

        return [.. payload];
    }

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;

    private byte GetCommandIdLowByte() {
        if (DeviceInfo.SupportsButtonGesture(NuraButtonGesture.TripleTap)) {
            return 0x74;
        }

        return DeviceInfo.FirmwareVersion > 510 ? (byte)0xB6 : (byte)0x50;
    }

    private byte SerializeRequiredBinding(NuraButtonSide side, NuraButtonGesture gesture) {
        var binding = Configuration.GetBinding(side, gesture);
        if (binding is null) {
            throw new InvalidOperationException($"Button configuration requires a binding for {side} {gesture} on {DeviceInfo.TypeName}.");
        }

        return NuraButtonFunctionCodec.ToRawByte(binding.Value);
    }
}
