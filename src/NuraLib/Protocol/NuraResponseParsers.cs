namespace NuraLib.Protocol;

internal static class NuraResponseParsers {
    private static byte[] RequireMinimumLength(byte[] payload, int minimumLength, string description) {
        if (payload.Length < minimumLength) {
            throw new InvalidOperationException($"{description} response was too short: {payload.Length}");
        }

        return payload;
    }

    private static byte[] RequireExactLength(byte[] payload, int expectedLength, string description) {
        if (payload.Length != expectedLength) {
            throw new InvalidOperationException($"{description} response length {payload.Length} did not match expected {expectedLength}.");
        }

        return payload;
    }

    private static byte ReadRequiredByte(byte[] payload, string description) {
        return RequireMinimumLength(payload, 1, description)[0];
    }

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
        return ReadRequiredByte(payload, "current profile");
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
        RequireMinimumLength(payload, 2, "ANC state");

        var ancEnabled = payload[0] != 0x00;
        var passthroughEnabled = payload[1] != 0x00;

        return new Devices.NuraAncState {
            AncEnabled = ancEnabled,
            PassthroughEnabled = passthroughEnabled
        };
    }

    public static int DecodeAncLevel(byte[] payload) {
        return ReadRequiredByte(payload, "ANC level");
    }

    public static bool DecodeBooleanFlag(byte[] payload) => ReadRequiredByte(payload, "Boolean") != 0x00;

    public static Devices.NuraPersonalisationMode DecodePersonalisedMode(byte[] payload) {
        return ReadRequiredByte(payload, "Personalised mode") == 0x00
            ? Devices.NuraPersonalisationMode.Personalised
            : Devices.NuraPersonalisationMode.Neutral;
    }

    public static NuraKickitState DecodeKickitState(byte[] payload) {
        RequireMinimumLength(payload, 2, "Kickit state");

        return new NuraKickitState(payload[0], payload[1] == 0x01);
    }

    public static Devices.NuraButtonConfiguration DecodeButtonConfiguration(
        byte[] payload,
        bool supportsDoubleTap,
        bool supportsTripleTap) {
        var expectedLength = supportsTripleTap
            ? 8
            : supportsDoubleTap
                ? 6
                : 2;

        RequireExactLength(payload, expectedLength, "Button configuration");

        return new Devices.NuraButtonConfiguration {
            LeftSingleTap = NuraButtonFunctionCodec.FromRawByte(payload[0]),
            RightSingleTap = NuraButtonFunctionCodec.FromRawByte(payload[1]),
            LeftDoubleTap = supportsDoubleTap ? NuraButtonFunctionCodec.FromRawByte(payload[2]) : null,
            RightDoubleTap = supportsDoubleTap ? NuraButtonFunctionCodec.FromRawByte(payload[3]) : null,
            LeftTapAndHold = supportsDoubleTap ? NuraButtonFunctionCodec.FromRawByte(payload[4]) : null,
            RightTapAndHold = supportsDoubleTap ? NuraButtonFunctionCodec.FromRawByte(payload[5]) : null,
            LeftTripleTap = supportsTripleTap ? NuraButtonFunctionCodec.FromRawByte(payload[6]) : null,
            RightTripleTap = supportsTripleTap ? NuraButtonFunctionCodec.FromRawByte(payload[7]) : null
        };
    }

    public static Devices.NuraDialConfiguration DecodeDialConfiguration(byte[] payload) {
        var normalizedPayload = payload.Length switch {
            2 => payload,
            6 => payload[..2],
            _ => throw new InvalidOperationException($"Dial configuration response length {payload.Length} was invalid.")
        };

        return new Devices.NuraDialConfiguration {
            Left = NuraDialFunctionCodec.FromRawByte(normalizedPayload[0]),
            Right = NuraDialFunctionCodec.FromRawByte(normalizedPayload[1])
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

internal readonly record struct NuraKickitState(int RawLevel, bool Enabled);

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
