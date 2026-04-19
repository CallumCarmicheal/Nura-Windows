using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class SetKickitEnabledCommand : NuraAppEncryptedCommand<byte[]> {
    public SetKickitEnabledCommand(bool enabled) {
        Enabled = enabled;
    }

    public bool Enabled { get; }

    public override string Name => $"SetKickitEnabled(enabled={Enabled})";

    protected override byte[] CreatePlainPayload() => [0x00, 0xb3, Enabled ? (byte)0x01 : (byte)0x00];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
