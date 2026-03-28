namespace NuraLib.Devices;

public sealed record class NuraDeviceInfo {
    public string TypeTag { get; init; } = "nuraphone";

    public string TypeName { get; init; } = "Nuraphone";

    public NuraDeviceType DeviceType { get; init; } = NuraDeviceType.Nuraphone;

    public string DeviceAddress { get; init; } = string.Empty;

    public string Serial { get; init; } = string.Empty;

    public int FirmwareVersion { get; init; }

    public int MaxPacketLengthHint { get; init; }

    public bool IsTws { get; init; }

    public int MinimumFirmwareVersion { get; init; }

    public int MinimumFirmwareVersionForOfflineMode { get; init; }

    public int DefaultImmersionLevel { get; init; }

    public NuraSupportedFeatures SupportedFeatures { get; init; } = NuraSupportedFeatures.None;

    public bool Supports(NuraSupportedFeatures feature) => SupportedFeatures.HasFlag(feature);
}
