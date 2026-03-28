namespace NuraLib.Configuration;

public sealed record class NuraDeviceConfig {
    public string Type { get; init; } = "Nuraphone";

    public required string DeviceAddress { get; init; }

    public required string DeviceSerial { get; init; }

    public int FirmwareVersion { get; init; }

    public int MaxPacketLengthHint { get; init; } = 182;

    public required string DeviceKey { get; init; }

    public byte[] GetRequiredDeviceKeyBytes() {
        try {
            var bytes = Convert.FromBase64String(DeviceKey);
            if (bytes.Length != 16) {
                throw new InvalidOperationException("deviceKey must decode to 16 bytes");
            }

            return bytes;
        } catch (FormatException ex) {
            throw new InvalidOperationException("deviceKey must be valid base64", ex);
        }
    }

    public string GetDeviceKeyHex() {
        return Utilities.HexEncoding.Format(GetRequiredDeviceKeyBytes());
    }

    public NuraDeviceConfig WithDeviceKeyBytes(byte[] keyBytes) {
        if (keyBytes.Length != 16) {
            throw new InvalidOperationException("deviceKey must be 16 bytes");
        }

        return this with {
            DeviceKey = Convert.ToBase64String(keyBytes)
        };
    }
}
