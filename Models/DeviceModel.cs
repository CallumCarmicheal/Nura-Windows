namespace NuraPopupWpf.Models;

public sealed class DeviceModel {
    public DeviceModel(
        string id,
        string name,
        int batteryLevel,
        string serialNumber,
        string softwareVersion,
        IReadOnlyList<ProfileModel> profiles
    ) {
        Id = id;
        Name = name;
        BatteryLevel = batteryLevel;
        SerialNumber = serialNumber;
        SoftwareVersion = softwareVersion;
        Profiles = profiles;
    }

    public string Id { get; }

    public string Name { get; }

    public int BatteryLevel { get; }

    public string SerialNumber { get; }

    public string SoftwareVersion { get; }

    public IReadOnlyList<ProfileModel> Profiles { get; }

    public override string ToString() => Name;
}
