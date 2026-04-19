namespace NuraLib.Protocol;

internal sealed class SetTemporaryAncStateCommand : NuraAppEncryptedCommand<byte[]> {
    public SetTemporaryAncStateCommand(bool ancEnabled, bool passthroughEnabled) {
        AncEnabled = ancEnabled;
        PassthroughEnabled = passthroughEnabled;
    }

    public bool AncEnabled { get; }

    public bool PassthroughEnabled { get; }

    public override string Name => $"SetTemporaryANCState(anc={AncEnabled},passthrough={PassthroughEnabled})";

    protected override byte[] CreatePlainPayload() => [
        0x00,
        0x4A,
        AncEnabled ? (byte)0x01 : (byte)0x00,
        PassthroughEnabled ? (byte)0x01 : (byte)0x00
    ];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
