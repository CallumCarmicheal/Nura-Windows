using NuraLib.Configuration;

namespace NuraLib.Devices;

/// <summary>
/// Represents a known Nura device, whether currently connected or only present in persisted configuration.
/// </summary>
public class NuraDevice {

    internal NuraDeviceConfig Config { get; }

    /// <summary>
    /// Gets static and capability information about the device.
    /// </summary>
    public NuraDeviceInfo Info { get; }

    /// <summary>
    /// Gets a value indicating whether a persistent device key is stored for this device.
    /// </summary>
    public bool HasPersistentDeviceKey => !string.IsNullOrWhiteSpace(Config.DeviceKey);


    internal NuraDevice(NuraDeviceConfig config) {
        Config = config;
        var deviceType = NuraDeviceCapabilities.ResolveType(config.Type);
        var capabilityInfo = NuraDeviceCapabilities.GetCapabilityInfo(deviceType, config.FirmwareVersion);
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
            SupportedFeatures = capabilityInfo.Features,
            Capabilities = capabilityInfo,
            SupportedButtonGestures = capabilityInfo.ButtonGestures
        };
    }
}
