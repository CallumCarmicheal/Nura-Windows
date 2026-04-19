namespace NuraLib.Protocol;

internal sealed class SetSpatialStateCommand : NuraAppEncryptedCommand<byte[]> {
    public SetSpatialStateCommand(bool enabled) {
        Enabled = enabled;
    }

    public bool Enabled { get; }

    public override string Name => $"SetSpatialState(enabled={Enabled})";

    protected override byte[] CreatePlainPayload() => [0x01, 0x7b, Enabled ? (byte)0x01 : (byte)0x00];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
