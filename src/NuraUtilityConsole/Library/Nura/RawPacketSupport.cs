using System.Buffers.Binary;

using NuraDesktopConsole.Library.Protocol;

namespace NuraDesktopConsole.Library.Nura;

internal static class RawPacketSupport {
    private const ushort NuraVendorId = 0x6872;

    internal static GaiaFrame BuildFrameFromPacketHex(string packetHex, out string mode, byte version = 0x01, byte flags = 0x00) {
        var bytes = Hex.Parse(packetHex);
        return BuildFrameFromPacketBytes(bytes, out mode, version, flags);
    }

    internal static GaiaFrame BuildFrameFromPacketBytes(byte[] bytes, out string mode, byte version = 0x01, byte flags = 0x00) {
        if (bytes.Length >= 8 && bytes[0] == 0xFF) {
            var parsed = GaiaResponse.Parse(bytes);
            mode = "full_frame";
            return new GaiaFrame {
                CommandId = (GaiaCommandId)parsed.CommandId,
                Bytes = bytes
            };
        }

        if (bytes.Length < 4) {
            throw new InvalidOperationException("raw packet must be either a full GAIA frame or at least 4 bytes of vendor+command data");
        }

        var vendorId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2));
        if (vendorId != NuraVendorId) {
            throw new InvalidOperationException($"unsupported raw vendor 0x{vendorId:x4}; expected 0x{NuraVendorId:x4}");
        }

        var rawCommandId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2));
        var payload = bytes.Length == 4 ? Array.Empty<byte>() : bytes[4..];
        mode = "raw_command";
        return GaiaPackets.BuildRawCommand(rawCommandId, payload, version, flags);
    }
}
