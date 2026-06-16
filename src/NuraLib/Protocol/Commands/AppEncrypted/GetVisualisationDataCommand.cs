namespace NuraLib.Protocol;

internal sealed class GetVisualisationDataCommand : NuraAppEncryptedCommand<Devices.NuraProfileVisualisationData> {
    public override string Name => "GetVisualisationData";

    protected override byte[] CreatePlainPayload() => [0x00, 0x65];

    protected override Devices.NuraProfileVisualisationData ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeVisualisationData(plainPayload);
}
