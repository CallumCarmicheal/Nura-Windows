using System.Buffers.Binary;

using NuraLib.Utilities;

namespace NuraLib.Protocol;

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

    public byte[] Data {
        get {
            var data = new byte[4 + Payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, 2), VendorId);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2, 2), RawCommandId);
            Payload.CopyTo(data, 4);
            return data;
        }
    }

    public static GaiaResponse Parse(byte[] bytes) {
        if (bytes.Length < 8) {
            throw new InvalidOperationException("frame too short");
        }

        if (bytes[0] != 0xFF) {
            throw new InvalidOperationException("invalid SOF");
        }

        var flags = bytes[2];
        var usesLengthExtension = (flags & 0x02) != 0;
        var headerLength = usesLengthExtension ? 9 : 8;
        if (bytes.Length < headerLength) {
            throw new InvalidOperationException("frame too short for header");
        }

        var length = usesLengthExtension
            ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(3, 2))
            : bytes[3];
        if (bytes.Length != length + headerLength) {
            throw new InvalidOperationException("length mismatch");
        }

        var vendorOffset = usesLengthExtension ? 5 : 4;
        var commandOffset = usesLengthExtension ? 7 : 6;
        var payloadOffset = usesLengthExtension ? 9 : 8;
        var rawCommandId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(commandOffset, 2));
        return new GaiaResponse {
            Sof = bytes[0],
            Version = bytes[1],
            Flags = flags,
            Length = length,
            VendorId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(vendorOffset, 2)),
            RawCommandId = rawCommandId,
            CommandId = (ushort)(rawCommandId & 0x1FFF),
            Payload = bytes[payloadOffset..]
        };
    }

    public override string ToString() {
        return $"command=0x{CommandId:x4} status=0x{Status:x2} payload={HexEncoding.Format(Payload)}";
    }
}
