namespace NuraLib.Configuration;

/// <summary>
/// Persisted durable configuration for a single Nura device.
/// </summary>
public sealed record class NuraDeviceConfig {
    /// <summary>
    /// Gets the raw device type label used to derive the normalized <c>NuraDeviceType</c>.
    /// </summary>
    public string Type { get; init; } = "Nuraphone";

    /// <summary>
    /// Gets the Bluetooth device address.
    /// </summary>
    public required string DeviceAddress { get; init; }

    /// <summary>
    /// Gets the device serial number.
    /// </summary>
    public required string DeviceSerial { get; init; }

    /// <summary>
    /// Gets the last known firmware version for the device.
    /// </summary>
    public int FirmwareVersion { get; init; }

    /// <summary>
    /// Gets the last known maximum packet-length hint for the device.
    /// </summary>
    public int MaxPacketLengthHint { get; init; } = 182;

    /// <summary>
    /// Gets a value indicating whether the host knows this device is a NuraNow device that must
    /// periodically phone home to refresh its backend entitlement.
    /// </summary>
    /// <remarks>
    /// The library does not currently auto-detect NuraNow status from device metadata or backend responses.
    /// Hosts should set this explicitly when they know the device is subject to the monthly provisioning policy.
    /// </remarks>
    public bool IsNuraNowDevice { get; init; }

    /// <summary>
    /// Gets the last successful backend-assisted provisioning timestamp, when known.
    /// </summary>
    public DateTimeOffset? LastProvisionedUtc { get; init; }

    /// <summary>
    /// Gets the persistent device key encoded as base64.
    /// </summary>
    public required string DeviceKey { get; init; }

    /// <summary>
    /// Decodes the configured persistent device key to its required 16-byte form.
    /// </summary>
    /// <returns>The decoded 16-byte device key.</returns>
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

    /// <summary>
    /// Returns the persistent device key as lowercase hexadecimal text.
    /// </summary>
    public string GetDeviceKeyHex() {
        return Utilities.HexEncoding.Format(GetRequiredDeviceKeyBytes());
    }

    /// <summary>
    /// Creates a copy of the device configuration with a new persistent device key.
    /// </summary>
    /// <param name="keyBytes">The new 16-byte device key.</param>
    /// <returns>A new configuration instance containing the supplied key.</returns>
    public NuraDeviceConfig WithDeviceKeyBytes(byte[] keyBytes) {
        if (keyBytes.Length != 16) {
            throw new InvalidOperationException("deviceKey must be 16 bytes");
        }

        return this with {
            DeviceKey = Convert.ToBase64String(keyBytes)
        };
    }
}
