using NuraLib.Configuration;
using NuraLib.Logging;
using NuraLib.Transport;
using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

/// <summary>
/// Manages the known and currently connected Nura device set for a client instance.
/// </summary>
public sealed class NuraDeviceManager {
    private const string Source = nameof(NuraDeviceManager);
    private readonly NuraConfigState _state;
    private readonly NuraClientLogger _logger;
    private readonly Auth.NuraAuthManager _authManager;
    private readonly Dictionary<string, ConnectedNuraDevice> _knownDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<NuraDevice> _all = [];
    private readonly List<ConnectedNuraDevice> _connected = [];

    internal NuraDeviceManager(NuraConfigState state, NuraClientLogger logger, Auth.NuraAuthManager authManager) {
        _state = state;
        _logger = logger;
        _authManager = authManager;
        ReloadKnownDevices();
    }

    /// <summary>
    /// Gets all devices currently known to the library from persisted configuration and active discovery state.
    /// </summary>
    public IReadOnlyList<NuraDevice> All => _all;

    /// <summary>
    /// Gets the subset of devices that are currently connected and available for active operations.
    /// </summary>
    public IReadOnlyList<ConnectedNuraDevice> Connected => _connected;

    /// <summary>
    /// Refreshes the known device inventory using Bluetooth discovery and connected-device probing.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task RefreshAsync(CancellationToken cancellationToken = default) {
        return RefreshCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the first known device with the specified serial number.
    /// </summary>
    /// <param name="serial">The device serial number to match.</param>
    /// <returns>The matching device, or <see langword="null"/> when no match is found.</returns>
    public NuraDevice? FindBySerial(string serial) {
        return _all.FirstOrDefault(device =>
            string.Equals(device.Info.Serial, serial, StringComparison.OrdinalIgnoreCase));
    }

    private void ReloadKnownDevices() {
        _knownDevices.Clear();
        foreach (var device in _state.Configuration.Devices) {
            var stableDevice = CreateConnectedDevice(device);
            stableDevice.IsConnected = false;
            _knownDevices[GetIdentityKey(device)] = stableDevice;
        }

        RebuildDeviceViews(_state.Configuration.Devices);
    }

    internal async Task ReplaceConnectedDevicesAsync(
        IEnumerable<NuraDeviceConfig> allDevices,
        IEnumerable<NuraDeviceConfig> connectedDevices) {
        var allDeviceList = allDevices.ToList();
        var connectedKeys = connectedDevices
            .Select(GetIdentityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentKeys = allDeviceList
            .Select(GetIdentityKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedKeys = _knownDevices.Keys
            .Where(key => !currentKeys.Contains(key))
            .ToList();

        foreach (var removedKey in removedKeys) {
            var removedDevice = _knownDevices[removedKey];
            await removedDevice.ApplyConnectionStateAsync(false);
            _knownDevices.Remove(removedKey);
        }

        foreach (var device in allDeviceList) {
            var stableDevice = GetOrCreateDevice(device);
            stableDevice.UpdateConfig(device);
            await stableDevice.ApplyConnectionStateAsync(connectedKeys.Contains(GetIdentityKey(device)));
        }

        RebuildDeviceViews(allDeviceList);
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.Information(Source, "Refreshing connected Nura devices.");

        var discovered = BluetoothDeviceProbe.FindConnectedNuraDevices();
        var knownByAddress = _state.Configuration.Devices.ToDictionary(
            device => device.DeviceAddress,
            StringComparer.OrdinalIgnoreCase);

        var merged = new List<NuraDeviceConfig>();
        var connected = new List<NuraDeviceConfig>();

        foreach (var known in _state.Configuration.Devices) {
            if (merged.All(existing => !string.Equals(existing.DeviceSerial, known.DeviceSerial, StringComparison.OrdinalIgnoreCase))) {
                merged.Add(known);
            }
        }

        foreach (var device in discovered) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!knownByAddress.TryGetValue(device.Address, out var config)) {
                var details = await BluetoothDeviceProbe.ProbeDeviceInfoAsync(device, _logger, cancellationToken);
                if (details is not null) {
                    var firmwareVersion = details.ExtendedInfo?.FirmwareVersion ?? details.DeviceInfo.FirmwareVersion;
                    var deviceType = NuraDeviceCapabilities.GetDebugName(NuraDeviceCapabilities.ResolveType(details.Device.Name));
                    config = new NuraDeviceConfig {
                        Type = deviceType,
                        DeviceAddress = device.Address,
                        DeviceSerial = details.DeviceInfo.SerialNumber.ToString(),
                        FirmwareVersion = firmwareVersion,
                        MaxPacketLengthHint = details.DeviceInfo.SerialNumber switch {
                            >= 0 and < 20000000 => 182,
                            _ => 70
                        },
                        IsNuraNowDevice = false,
                        LastProvisionedUtc = null,
                        DeviceKey = string.Empty
                    };
                    merged.RemoveAll(existing => string.Equals(existing.DeviceAddress, config.DeviceAddress, StringComparison.OrdinalIgnoreCase));
                    merged.Add(config);
                } else {
                    config = new NuraDeviceConfig {
                        Type = device.Name,
                        DeviceAddress = device.Address,
                        DeviceSerial = device.Address.Replace(":", string.Empty, StringComparison.Ordinal),
                        FirmwareVersion = 0,
                        MaxPacketLengthHint = 182,
                        IsNuraNowDevice = false,
                        LastProvisionedUtc = null,
                        DeviceKey = string.Empty
                    };
                    merged.RemoveAll(existing => string.Equals(existing.DeviceAddress, config.DeviceAddress, StringComparison.OrdinalIgnoreCase));
                    merged.Add(config);
                }
            }

            connected.Add(config);
        }

        var currentConfig = _state.Configuration;
        var updatedDevices = merged
            .GroupBy(GetIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(device => device.DeviceSerial, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await ReplaceConnectedDevicesAsync(updatedDevices, connected);

        if (!currentConfig.Devices.SequenceEqual(updatedDevices)) {
            _state.ReplaceConfiguration(
                currentConfig with { Devices = updatedDevices },
                NuraStateSaveReason.DeviceInventory,
                "Updated device inventory from Bluetooth refresh.");
        }
    }

    private ConnectedNuraDevice GetOrCreateDevice(NuraDeviceConfig config) {
        var key = GetIdentityKey(config);
        if (_knownDevices.TryGetValue(key, out var existing)) {
            return existing;
        }

        var device = CreateConnectedDevice(config);
        _knownDevices[key] = device;
        return device;
    }

    private ConnectedNuraDevice CreateConnectedDevice(NuraDeviceConfig config) {
        return new ConnectedNuraDevice(_state, _authManager, config, _logger);
    }

    private void RebuildDeviceViews(IEnumerable<NuraDeviceConfig> orderedDevices) {
        _all.Clear();
        _connected.Clear();

        foreach (var config in orderedDevices) {
            var device = _knownDevices[GetIdentityKey(config)];
            _all.Add(device);
            if (device.IsConnected) {
                _connected.Add(device);
            }
        }
    }

    private static string GetIdentityKey(NuraDeviceConfig config) {
        var serial = config.DeviceSerial?.Trim() ?? string.Empty;
        var address = config.DeviceAddress?.Trim() ?? string.Empty;
        return $"{serial}|{address}";
    }
}
