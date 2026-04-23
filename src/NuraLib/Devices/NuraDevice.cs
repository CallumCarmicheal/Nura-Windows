using NuraLib.Configuration;

namespace NuraLib.Devices;

/// <summary>
/// Represents a known Nura device, whether currently connected or only present in persisted configuration.
/// </summary>
public class NuraDevice {

    internal NuraDeviceConfig Config { get; private set; } = null!;

    /// <summary>
    /// Gets static and capability information about the device.
    /// </summary>
    public NuraDeviceInfo Info { get; private set; } = null!;

    /// <summary>
    /// Gets a value indicating whether the device is currently present in the active Bluetooth inventory.
    /// </summary>
    public bool IsConnected { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether a persistent device key is stored for this device.
    /// </summary>
    public bool HasPersistentDeviceKey => !string.IsNullOrWhiteSpace(Config.DeviceKey);

    /// <summary>
    /// Gets a value indicating whether this device is configured as a NuraNow device by the host.
    /// </summary>
    public bool IsNuraNowDevice => Config.IsNuraNowDevice;

    /// <summary>
    /// Gets the last successful backend-assisted provisioning timestamp, when known.
    /// </summary>
    public DateTimeOffset? LastProvisionedUtc => Config.LastProvisionedUtc;


    internal NuraDevice(NuraDeviceConfig config) {
        ApplyConfig(config);
    }

    internal void UpdateConfig(NuraDeviceConfig config) {
        ApplyConfig(config);
    }

    private void ApplyConfig(NuraDeviceConfig config) {
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
