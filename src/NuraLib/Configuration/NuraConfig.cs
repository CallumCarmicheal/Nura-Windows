namespace NuraLib.Configuration;

/// <summary>
/// Root durable configuration model for <c>NuraLib</c>.
/// </summary>
public sealed record class NuraConfig {
    /// <summary>
    /// Gets the base API URL used for backend-assisted authentication and provisioning.
    /// </summary>
    public string ApiBase { get; init; } = "https://api-p1.nuraphone.com/";

    /// <summary>
    /// Gets the client-instance identifier used when talking to the Nura backend.
    /// </summary>
    public string Uuid { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Gets the persisted authentication state used for online operations.
    /// </summary>
    public NuraAuthConfig Auth { get; init; } = new();

    /// <summary>
    /// Gets the persisted per-device configuration list.
    /// </summary>
    public List<NuraDeviceConfig> Devices { get; init; } = [];

    /// <summary>
    /// Finds the configured device with the specified serial number.
    /// </summary>
    /// <param name="deviceSerial">The target device serial number.</param>
    /// <returns>The matching configured device.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the device is not present in the configuration.</exception>
    public NuraDeviceConfig GetRequiredDeviceBySerial(string deviceSerial) {
        var match = Devices.FirstOrDefault(device =>
            string.Equals(device.DeviceSerial, deviceSerial, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException($"device not found for serial: {deviceSerial}");
    }
}
