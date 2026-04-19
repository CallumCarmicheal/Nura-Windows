using NuraLib.Utilities.Docs;

namespace NuraLib.Monitoring;

/// <summary>
/// Provides connection-level monitoring for Nura devices.
/// </summary>
public sealed class NuraMonitoringManager {
    /// <summary>
    /// Raised when a device becomes connected and visible to the monitoring layer.
    /// </summary>
    public event EventHandler<NuraDeviceConnectionEventArgs>? DeviceConnected;

    /// <summary>
    /// Raised when a previously connected device disconnects.
    /// </summary>
    public event EventHandler<NuraDeviceConnectionEventArgs>? DeviceDisconnected;

    [BluetoothImplementationRequired("Monitoring", Notes = "Needs Bluetooth connection/disconnection event integration.")]
    /// <summary>
    /// Starts monitoring Bluetooth connection lifecycle events for Nura devices.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task StartAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth monitoring has not been wired into NuraLib yet.");
    }

    /// <summary>
    /// Stops connection lifecycle monitoring.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
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
