namespace NuraLib.Protocol;

internal sealed class SetGlobalAncEnabledCommand : NuraAppEncryptedCommand<byte[]> {
    public SetGlobalAncEnabledCommand(int profileId, bool enabled) {
        ProfileId = profileId;
        Enabled = enabled;
    }

    public int ProfileId { get; }

    public bool Enabled { get; }

    public override string Name => $"SetGlobalANCEnable(profile={ProfileId}, enabled={Enabled})";

    protected override byte[] CreatePlainPayload() =>
        [0x01, 0x1a, checked((byte)ProfileId), Enabled ? (byte)0x01 : (byte)0x00];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
