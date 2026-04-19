using System.Buffers.Binary;

using NuraLib.Crypto;

namespace NuraLib.Protocol;

internal static class GaiaPacketFactory {
    private const byte Sof = 0xFF;
    private const byte Version1 = 0x01;
    private const ushort NuraVendorId = 0x6872;

    public static GaiaFrame CreateCommand(GaiaCommandId commandId, byte[]? payload = null)
        => CreateRawCommand((ushort)commandId, payload);

    public static GaiaFrame CreateRawCommand(
        ushort rawCommandId,
        byte[]? payload = null,
        byte version = Version1,
        byte flags = 0x00) {
        payload ??= Array.Empty<byte>();

        var usesLengthExtension = (flags & 0x02) != 0;
        var headerLength = usesLengthExtension ? 9 : 8;
        var frame = new byte[headerLength + payload.Length];
        frame[0] = Sof;
        frame[1] = version;
        frame[2] = flags;
        if (usesLengthExtension) {
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3, 2), (ushort)payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5, 2), NuraVendorId);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7, 2), rawCommandId);
            payload.CopyTo(frame, 9);
        } else {
            frame[3] = (byte)payload.Length;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), NuraVendorId);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), rawCommandId);
            payload.CopyTo(frame, 8);
        }

        return new GaiaFrame {
            CommandId = (GaiaCommandId)(rawCommandId & 0x1FFF),
            Bytes = frame
        };
    }

    public static GaiaFrame CreateAuthenticatedAppCommand(NuraSessionCrypto crypto, byte[] appPayload) =>
        CreateCommand(GaiaCommandId.EntryAppEncryptedAuthenticated, crypto.EncryptAuthenticated(appPayload));

    public static GaiaFrame CreateUnauthenticatedAppCommand(NuraSessionCrypto crypto, byte[] appPayload) =>
        CreateCommand(GaiaCommandId.EntryAppEncryptedUnauthenticated, crypto.EncryptUnauthenticated(appPayload));
}
