namespace NuraLib.Protocol;

internal static class NuraResponseParsers {
    public static byte[] DecryptAuthenticatedPlainPayload(Crypto.NuraSessionRuntime runtime, GaiaResponse response) {
        var plain = runtime.Crypto.DecryptAuthenticated(response.PayloadExcludingStatus);
        return plain.Length <= 1 ? Array.Empty<byte>() : plain[1..];
    }

    public static bool TryDecodeDeviceInfo(byte[] payload, out DeviceInfo deviceInfo) {
        if (payload.Length != 8) {
            deviceInfo = default;
            return false;
        }

        deviceInfo = new DeviceInfo(ReadInt32BigEndian(payload, 0), ReadInt32BigEndian(payload, 4));
        return true;
    }

    public static bool TryDecodeExtendedDeviceInfo(byte[] payload, out ExtendedDeviceInfo extendedDeviceInfo) {
        if (payload.Length != 20) {
            extendedDeviceInfo = default;
            return false;
        }

        var serialNumber = ReadInt32BigEndian(payload, 0);
        var firmwareVersion = ReadInt32BigEndian(payload, 4);
        var baseSerial = ReadInt32BigEndian(payload, 8);
        var number = ReadInt32BigEndian(payload, 12);
        var peerSerial = ReadInt32BigEndian(payload, 16);
        var peerRssi = ReadInt16BigEndian(payload, 18);

        extendedDeviceInfo = new ExtendedDeviceInfo(serialNumber, firmwareVersion, baseSerial, number, peerSerial, peerRssi);
        return true;
    }

    public static int DecodeCurrentProfileId(byte[] payload) {
        if (payload.Length < 1) {
            throw new InvalidOperationException("current profile response was empty");
        }

        return payload[0];
    }

    public static string DecodeProfileName(byte[] payload) {
        if (payload.Length == 0) {
            return string.Empty;
        }

        var terminatorIndex = Array.IndexOf(payload, (byte)0x00);
        var length = terminatorIndex >= 0 ? terminatorIndex : payload.Length;
        return System.Text.Encoding.UTF8.GetString(payload, 0, length);
    }

    public static Devices.NuraAncState DecodeAncState(byte[] payload) {
        if (payload.Length < 2) {
            throw new InvalidOperationException($"ANC state response was too short: {payload.Length}");
        }

        var ancEnabled = payload[0] != 0x00;
        var passthroughEnabled = payload[1] != 0x00;

        return new Devices.NuraAncState {
            AncEnabled = ancEnabled,
            PassthroughEnabled = passthroughEnabled
        };
    }

    public static int DecodeUInt16BigEndian(byte[] payload) => payload.Length < 2 ? 0 : (payload[0] << 8) | payload[1];

    private static int ReadInt32BigEndian(byte[] payload, int offset) {
        return
            (payload[offset] << 24) |
            (payload[offset + 1] << 16) |
            (payload[offset + 2] << 8) |
            payload[offset + 3];
    }

    private static int ReadUInt16BigEndian(byte[] payload, int offset) {
        return (payload[offset] << 8) | payload[offset + 1];
    }

    private static short ReadInt16BigEndian(byte[] payload, int offset) {
        return unchecked((short)ReadUInt16BigEndian(payload, offset));
    }
}

internal readonly record struct DeviceInfo(int SerialNumber, int FirmwareVersion);

internal readonly record struct ExtendedDeviceInfo(
    int SerialNumber,
    int FirmwareVersion,
    int BaseSerial,
    int Number,
    int PeerSerial,
    int PeerRssi) {
    public bool PeerConnected => PeerRssi > short.MinValue;
    public bool RightIsPrimary => SerialNumber == BaseSerial;
    public bool LeftIsPrimary => !RightIsPrimary;
    public bool RightConnected => RightIsPrimary || (LeftIsPrimary && PeerConnected);
    public bool LeftConnected => LeftIsPrimary || (RightIsPrimary && PeerConnected);
}
