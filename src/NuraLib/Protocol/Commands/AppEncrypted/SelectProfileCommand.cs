namespace NuraLib.Protocol;

internal sealed class SelectProfileCommand : NuraAppEncryptedCommand<byte[]> {
    public SelectProfileCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"SelectProfile(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x00, 0x1B, checked((byte)ProfileId)];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
