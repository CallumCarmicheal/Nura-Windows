using System.Buffers.Binary;

using desktop_app.Crypto;

namespace desktop_app.Protocol;

internal static class GaiaPackets {
    private const byte Sof = 0xFF;
    private const byte Version1 = 0x01;
    private const ushort NuraVendorId = 0x6872;

    public static GaiaFrame BuildCommand(GaiaCommandId commandId, byte[]? payload = null) {
        payload ??= Array.Empty<byte>();
        var rawPayload = new byte[4 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(rawPayload.AsSpan(0, 2), NuraVendorId);
        BinaryPrimitives.WriteUInt16BigEndian(rawPayload.AsSpan(2, 2), (ushort)commandId);
        payload.CopyTo(rawPayload, 4);

        var frame = new byte[8 + payload.Length];
        frame[0] = Sof;
        frame[1] = Version1;
        frame[2] = 0x00;
        frame[3] = (byte)payload.Length;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), NuraVendorId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(6, 2), (ushort)commandId);
        payload.CopyTo(frame, 8);

        return new GaiaFrame {
            CommandId = commandId,
            Bytes = frame
        };
    }

    public static GaiaFrame BuildAuthenticatedAppCommand(NuraSessionCrypto crypto, byte[] appPayload)
        => BuildCommand(GaiaCommandId.EntryAppEncryptedAuthenticated, crypto.EncryptAuthenticated(appPayload));

    public static GaiaFrame BuildUnauthenticatedAppCommand(NuraSessionCrypto crypto, byte[] appPayload)
        => BuildCommand(GaiaCommandId.EntryAppEncryptedUnauthenticated, crypto.EncryptUnauthenticated(appPayload));
}
