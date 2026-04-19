namespace NuraLib.Protocol;

internal sealed class SetKickitStateCommand : NuraAppEncryptedCommand<byte[]> {
    public SetKickitStateCommand(int profileId, int? levelRaw, bool? enabled) {
        if (levelRaw is < 0 or > 6) {
            throw new ArgumentOutOfRangeException(nameof(levelRaw), levelRaw, "Kickit level raw index must be between 0 and 6.");
        }

        ProfileId = profileId;
        LevelRaw = levelRaw;
        Enabled = enabled;
    }

    public int ProfileId { get; }

    public int? LevelRaw { get; }

    public bool? Enabled { get; }

    public override string Name => $"SetKickitState(profile={ProfileId}, levelRaw={LevelRaw?.ToString() ?? "null"}, enabled={Enabled?.ToString() ?? "null"})";

    protected override byte[] CreatePlainPayload() => [
        0x01,
        0x1d,
        checked((byte)ProfileId),
        LevelRaw.HasValue ? checked((byte)LevelRaw.Value) : (byte)0xff,
        Enabled.HasValue ? (Enabled.Value ? (byte)0x01 : (byte)0x00) : (byte)0xff
    ];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
