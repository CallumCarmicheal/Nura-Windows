namespace NuraLib.Protocol;

internal sealed class GetKickitParamsCommand : NuraAppEncryptedCommand<NuraClassicKickitParams> {
    public GetKickitParamsCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetKickitParams(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x00, 0x4d, checked((byte)ProfileId)];

    protected override NuraClassicKickitParams ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeClassicKickitParams(plainPayload);
}
