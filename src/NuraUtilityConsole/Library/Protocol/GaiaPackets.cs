using System.Buffers.Binary;

using NuraDesktopConsole.Library.Crypto;

namespace NuraDesktopConsole.Library.Protocol;

internal static class GaiaPackets {
    private const byte Sof = 0xFF;
    private const byte Version1 = 0x01;
    private const ushort NuraVendorId = 0x6872;

    public static GaiaFrame BuildCommand(GaiaCommandId commandId, byte[]? payload = null)
        => BuildRawCommand((ushort)commandId, payload);

    public static GaiaFrame BuildRawCommand(
        ushort rawCommandId,
        byte[]? payload = null,
        byte version = Version1,
        byte flags = 0x00) {
        payload ??= Array.Empty<byte>();
        var rawPayload = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(rawPayload.AsSpan(0, 2), NuraVendorId);
        BinaryPrimitives.WriteUInt16BigEndian(rawPayload.AsSpan(2, 2), rawCommandId);
        payload.CopyTo(rawPayload, 4);

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

    public static GaiaFrame BuildAuthenticatedAppCommand(NuraSessionCrypto crypto, byte[] appPayload)
        => BuildCommand(GaiaCommandId.EntryAppEncryptedAuthenticated, crypto.EncryptAuthenticated(appPayload));

    public static GaiaFrame BuildUnauthenticatedAppCommand(NuraSessionCrypto crypto, byte[] appPayload)
        => BuildCommand(GaiaCommandId.EntryAppEncryptedUnauthenticated, crypto.EncryptUnauthenticated(appPayload));
}
