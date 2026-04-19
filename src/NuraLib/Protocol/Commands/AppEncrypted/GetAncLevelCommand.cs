namespace NuraLib.Protocol;

internal sealed class GetAncLevelCommand : NuraAppEncryptedCommand<int> {
    public GetAncLevelCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetANCLevel(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x01, 0x02, checked((byte)ProfileId)];

    protected override int ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeAncLevel(plainPayload);
}
