namespace NuraLib.Protocol;

internal sealed class GaiaFrame {
    public required GaiaCommandId CommandId { get; init; }

    public required byte[] Bytes { get; init; }

    public override string ToString() => $"command=0x{(ushort)CommandId:x4} bytes={Utilities.HexEncoding.Format(Bytes)}";
}
