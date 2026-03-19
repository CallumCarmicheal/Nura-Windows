using System.Buffers.Binary;

namespace desktop_app.Protocol;

internal sealed class GaiaResponse {
    public required byte Sof { get; init; }

    public required byte Version { get; init; }

    public required byte Flags { get; init; }

    public required int Length { get; init; }

    public required ushort VendorId { get; init; }

    public required ushort RawCommandId { get; init; }

    public required ushort CommandId { get; init; }

    public required byte[] Payload { get; init; }

    public byte Status => Payload.Length == 0 ? (byte)0 : Payload[0];

    public byte[] PayloadExcludingStatus => Payload.Length <= 1 ? Array.Empty<byte>() : Payload[1..];

    public static GaiaResponse Parse(byte[] bytes) {
        if (bytes.Length < 8) {
            throw new InvalidOperationException("frame too short");
        }

        if (bytes[0] != 0xFF) {
            throw new InvalidOperationException("invalid SOF");
        }

        var length = bytes[3];
        if (bytes.Length != length + 8) {
            throw new InvalidOperationException("length mismatch");
        }

        var rawCommandId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6, 2));
        return new GaiaResponse {
            Sof = bytes[0],
            Version = bytes[1],
            Flags = bytes[2],
            Length = length,
            VendorId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)),
            RawCommandId = rawCommandId,
            CommandId = (ushort)(rawCommandId & 0x1FFF),
            Payload = bytes[8..]
        };
    }

    public string ToDisplayString() {
        return string.Join(Environment.NewLine,
        [
            $"sof=0x{Sof:x2}",
            $"version=0x{Version:x2}",
            $"flags=0x{Flags:x2}",
            $"length={Length}",
            $"vendor=0x{VendorId:x4}",
            $"command_raw=0x{RawCommandId:x4}",
            $"command=0x{CommandId:x4}",
            $"status=0x{Status:x2}",
            $"payload.hex={Hex.Format(Payload)}",
            $"payload_ex_status.hex={Hex.Format(PayloadExcludingStatus)}"
        ]);
    }
}
