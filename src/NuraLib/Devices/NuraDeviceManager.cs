using NuraLib.Configuration;
using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

public sealed class NuraDeviceManager {
    private readonly NuraConfigState _state;
    private readonly List<NuraDevice> _all = [];
    private readonly List<ConnectedNuraDevice> _connected = [];

    internal NuraDeviceManager(NuraConfigState state) {
        _state = state;
        ReloadKnownDevices();
    }

    public IReadOnlyList<NuraDevice> All => _all;

    public IReadOnlyList<ConnectedNuraDevice> Connected => _connected;

    [BluetoothImplementationRequired("Discovery", Notes = "Needs Bluetooth device discovery and hardware-info probing for connected Nura devices.")]
    public Task RefreshAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        ReloadKnownDevices();
        throw new NotImplementedException("Bluetooth device discovery has not been wired into NuraLib yet.");
    }

    public NuraDevice? FindBySerial(string serial) {
        return _all.FirstOrDefault(device =>
            string.Equals(device.Info.Serial, serial, StringComparison.OrdinalIgnoreCase));
    }

    private void ReloadKnownDevices() {
        _all.Clear();
        _connected.Clear();

        foreach (var device in _state.Configuration.Devices) {
            _all.Add(new NuraDevice(device));
        }
    }

    internal void ReplaceConnectedDevices(IEnumerable<NuraDeviceConfig> devices) {
        _all.Clear();
        _connected.Clear();

        foreach (var device in _state.Configuration.Devices) {
            _all.Add(new NuraDevice(device));
        }

        foreach (var device in devices) {
            var connected = new ConnectedNuraDevice(device);
            _connected.Add(connected);
            if (_all.All(existing => !string.Equals(existing.Info.Serial, connected.Info.Serial, StringComparison.OrdinalIgnoreCase))) {
                _all.Add(connected);
            }
        }
    }
}
