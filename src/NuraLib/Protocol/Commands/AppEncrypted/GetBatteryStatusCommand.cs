using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class GetBatteryStatusCommand : NuraAppEncryptedCommand<NuraBatteryStatus> {
    public override string Name => "ReadBattery";

    protected override byte[] CreatePlainPayload() => NuraQueryFactory.CreateReadBatteryPayload();

    protected override NuraBatteryStatus ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeBatteryStatus(plainPayload);
}
