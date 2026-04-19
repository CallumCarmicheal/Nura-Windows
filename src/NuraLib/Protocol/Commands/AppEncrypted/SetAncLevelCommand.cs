namespace NuraLib.Protocol;

internal sealed class SetAncLevelCommand : NuraAppEncryptedCommand<byte[]> {
    public SetAncLevelCommand(int profileId, int level) {
        if (level is < 0 or > 6) {
            throw new ArgumentOutOfRangeException(nameof(level), level, "ANC level must be between 0 and 6.");
        }

        ProfileId = profileId;
        Level = level;
    }

    public int ProfileId { get; }

    public int Level { get; }

    public override string Name => $"SetANCLevel(profile={ProfileId}, level={Level})";

    protected override byte[] CreatePlainPayload() => [0x01, 0x01, checked((byte)ProfileId), checked((byte)Level)];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
