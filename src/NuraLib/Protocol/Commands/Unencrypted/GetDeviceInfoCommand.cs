namespace NuraLib.Protocol;

internal sealed class GetDeviceInfoCommand : NuraUnencryptedCommand<DeviceInfo> {
    public override string Name => "GetDeviceInfo";

    public override GaiaCommandId ExpectedResponseCommandId => GaiaCommandId.GetDeviceInfo;

    protected override GaiaFrame CreateFrameCore() =>
        GaiaPacketFactory.CreateCommand(GaiaCommandId.GetDeviceInfo);

    protected override DeviceInfo ParseResponseCore(GaiaResponse response) {
        if (!NuraResponseParsers.TryDecodeDeviceInfo(response.PayloadExcludingStatus, out var deviceInfo)) {
            throw new InvalidOperationException("Failed to decode device info response.");
        }

        return deviceInfo;
    }
}
