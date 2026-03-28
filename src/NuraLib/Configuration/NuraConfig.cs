namespace NuraLib.Configuration;

public sealed record class NuraConfig {
    public string ApiBase { get; init; } = "https://api-p1.nuraphone.com/";

    public string Uuid { get; init; } = Guid.NewGuid().ToString();

    public NuraAuthConfig Auth { get; init; } = new();

    public List<NuraDeviceConfig> Devices { get; init; } = [];

    public NuraDeviceConfig GetRequiredDeviceBySerial(string deviceSerial) {
        var match = Devices.FirstOrDefault(device =>
            string.Equals(device.DeviceSerial, deviceSerial, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException($"device not found for serial: {deviceSerial}");
    }
}
