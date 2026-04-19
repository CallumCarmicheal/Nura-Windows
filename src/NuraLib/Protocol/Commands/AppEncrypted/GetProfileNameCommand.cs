namespace NuraLib.Protocol;

internal sealed class GetProfileNameCommand : NuraAppEncryptedCommand<string> {
    public GetProfileNameCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetProfileName(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x00, 0x1a, checked((byte)ProfileId)];

    protected override string ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeProfileName(plainPayload);
}
