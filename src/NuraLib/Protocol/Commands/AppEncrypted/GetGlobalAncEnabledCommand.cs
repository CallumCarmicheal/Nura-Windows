namespace NuraLib.Protocol;

internal sealed class GetGlobalAncEnabledCommand : NuraAppEncryptedCommand<bool> {
    public GetGlobalAncEnabledCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetGlobalANCEnable(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x01, 0x1b, checked((byte)ProfileId)];

    protected override bool ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeBooleanFlag(plainPayload);
}
