using NuraLib.Configuration;

namespace NuraLib.Devices;

/// <summary>
/// Represents a known Nura device, whether currently connected or only present in persisted configuration.
/// </summary>
public class NuraDevice {
    private NuraDeviceInfo _info = null!;
    private bool _isConnected;

    internal NuraDeviceConfig Config { get; private set; } = null!;

    /// <summary>
    /// Gets static and capability information about the device.
    /// </summary>
    public NuraDeviceInfo Info => _info;

    /// <summary>
    /// Gets a value indicating whether the device is currently present in the active Bluetooth inventory.
    /// </summary>
    public bool IsConnected => _isConnected;

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

    public event EventHandler<NuraValueChangedEventArgs<bool>>? IsConnectedChanged;

    public event EventHandler? InfoChanged;

    public event EventHandler<NuraValueChangedEventArgs<bool>>? HasPersistentDeviceKeyChanged;

    /// <summary>
    /// Raised when any cached public device value changes.
    /// </summary>
    public event EventHandler? Changed;


    internal NuraDevice(NuraDeviceConfig config) {
        ApplyConfig(config);
    }

    internal void UpdateConfig(NuraDeviceConfig config) {
        ApplyConfig(config);
    }

    internal void UpdateConnectionState(bool isConnected) {
        var previous = _isConnected;
        _isConnected = isConnected;
        if (previous != isConnected) {
            IsConnectedChanged?.Invoke(this, new NuraValueChangedEventArgs<bool>(previous, isConnected));
            RaiseChanged();
        }
    }

    internal void RaiseChanged() {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyConfig(NuraDeviceConfig config) {
        var previousInfo = _info;
        var previousHasPersistentDeviceKey = !ReferenceEquals(Config, null) && !string.IsNullOrWhiteSpace(Config.DeviceKey);

        Config = config;
        var deviceType = NuraDeviceCapabilities.ResolveType(config.Type);
        var capabilityInfo = NuraDeviceCapabilities.GetCapabilityInfo(deviceType, config.FirmwareVersion);
        _info = new NuraDeviceInfo {
            TypeTag = NuraDeviceCapabilities.GetTypeTag(deviceType),
            TypeName = NuraDeviceCapabilities.GetDebugName(deviceType),
            DeviceType = deviceType,
            FriendlyName = config.FriendlyName,
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

        var infoChanged = previousInfo is not null && !Equals(previousInfo, _info);
        if (infoChanged) {
            InfoChanged?.Invoke(this, EventArgs.Empty);
        }

        var currentHasPersistentDeviceKey = !string.IsNullOrWhiteSpace(config.DeviceKey);
        var persistentDeviceKeyChanged = previousInfo is not null && previousHasPersistentDeviceKey != currentHasPersistentDeviceKey;
        if (persistentDeviceKeyChanged) {
            HasPersistentDeviceKeyChanged?.Invoke(this, new NuraValueChangedEventArgs<bool>(previousHasPersistentDeviceKey, currentHasPersistentDeviceKey));
        }

        if (infoChanged || persistentDeviceKeyChanged) {
            RaiseChanged();
        }
    }
}
