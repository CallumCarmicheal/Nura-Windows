using NuraLib.Configuration;

namespace NuraLib.Devices;

public class NuraDevice {
    internal NuraDevice(NuraDeviceConfig config) {
        Config = config;
        var deviceType = NuraDeviceCapabilities.ResolveType(config.Type);
        Info = new NuraDeviceInfo {
            TypeTag = NuraDeviceCapabilities.GetTypeTag(deviceType),
            TypeName = NuraDeviceCapabilities.GetDebugName(deviceType),
            DeviceType = deviceType,
            DeviceAddress = config.DeviceAddress,
            Serial = config.DeviceSerial,
            FirmwareVersion = config.FirmwareVersion,
            MaxPacketLengthHint = config.MaxPacketLengthHint,
            IsTws = NuraDeviceCapabilities.IsTws(deviceType),
            MinimumFirmwareVersion = NuraDeviceCapabilities.GetMinimumFirmwareVersion(deviceType),
            MinimumFirmwareVersionForOfflineMode = NuraDeviceCapabilities.GetMinimumFirmwareVersionForOfflineMode(deviceType),
            DefaultImmersionLevel = NuraDeviceCapabilities.GetDefaultImmersionLevel(deviceType),
            SupportedFeatures = NuraDeviceCapabilities.GetSupportedFeatures(deviceType, config.FirmwareVersion)
        };
    }

    internal NuraDeviceConfig Config { get; }

    public NuraDeviceInfo Info { get; }

    public bool HasPersistentDeviceKey => !string.IsNullOrWhiteSpace(Config.DeviceKey);
}
