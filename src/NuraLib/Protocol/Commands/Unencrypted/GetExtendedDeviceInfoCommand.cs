namespace NuraLib.Protocol;

internal sealed class GetExtendedDeviceInfoCommand : NuraUnencryptedCommand<ExtendedDeviceInfo> {
    public override string Name => "GetExtendedDeviceInfo";

    public override GaiaCommandId ExpectedResponseCommandId => GaiaCommandId.GetExtendedDeviceInfo;

    protected override GaiaFrame CreateFrameCore() =>
        GaiaPacketFactory.CreateCommand(GaiaCommandId.GetExtendedDeviceInfo);

    protected override ExtendedDeviceInfo ParseResponseCore(GaiaResponse response) {
        if (!NuraResponseParsers.TryDecodeExtendedDeviceInfo(response.PayloadExcludingStatus, out var extendedInfo)) {
            throw new InvalidOperationException("Failed to decode extended device info response.");
        }

        return extendedInfo;
    }
}
