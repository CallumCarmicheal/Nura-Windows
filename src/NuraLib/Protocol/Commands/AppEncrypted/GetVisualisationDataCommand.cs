namespace NuraLib.Protocol;

internal sealed class GetVisualisationDataCommand : NuraAppEncryptedCommand<Devices.NuraProfileVisualisationData?> {
    public GetVisualisationDataCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetVisualisationData(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x00, 0xb8, checked((byte)ProfileId)];

    protected override Devices.NuraProfileVisualisationData? ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeVisualisationData(plainPayload);
}
