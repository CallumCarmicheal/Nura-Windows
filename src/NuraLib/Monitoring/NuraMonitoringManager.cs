using NuraLib.Utilities.Docs;

namespace NuraLib.Monitoring;

public sealed class NuraMonitoringManager {
    public event EventHandler<NuraDeviceConnectionEventArgs>? DeviceConnected;

    public event EventHandler<NuraDeviceConnectionEventArgs>? DeviceDisconnected;

    [BluetoothImplementationRequired("Monitoring", Notes = "Needs Bluetooth connection/disconnection event integration.")]
    public Task StartAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth monitoring has not been wired into NuraLib yet.");
    }

    public Task StopAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal void RaiseDeviceConnected(Devices.ConnectedNuraDevice device) {
        DeviceConnected?.Invoke(this, new NuraDeviceConnectionEventArgs(device));
    }

    internal void RaiseDeviceDisconnected(Devices.ConnectedNuraDevice device) {
        DeviceDisconnected?.Invoke(this, new NuraDeviceConnectionEventArgs(device));
    }
}
