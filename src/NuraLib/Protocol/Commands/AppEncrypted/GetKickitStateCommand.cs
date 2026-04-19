namespace NuraLib.Protocol;

internal sealed class GetKickitStateCommand : NuraAppEncryptedCommand<NuraKickitState> {
    public GetKickitStateCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetKickitState(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x01, 0x1e, checked((byte)ProfileId)];

    protected override NuraKickitState ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeKickitState(plainPayload);
}
