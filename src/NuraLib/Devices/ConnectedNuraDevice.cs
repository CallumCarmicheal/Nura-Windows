using NuraLib.Configuration;
using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

public sealed class ConnectedNuraDevice : NuraDevice {
    internal ConnectedNuraDevice(NuraDeviceConfig config)
            : base(config) 
    {
        State = new NuraDeviceState(this);
        Configuration = new NuraDeviceConfiguration();
        Profiles = new NuraProfiles();
    }

    public NuraDeviceState State { get; }

    public NuraDeviceConfiguration Configuration { get; }

    public NuraProfiles Profiles { get; }

    public Task<bool> RequiresProvisioningAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!HasPersistentDeviceKey);
    }

    [BluetoothImplementationRequired("Provisioning", Notes = "Needs full bootstrap flow through session/start_4 and persistent key storage.")]
    public Task EnsureProvisionedAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Device provisioning has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Connection", Notes = "Needs local encrypted connection and session setup using the persistent device key.")]
    public Task ConnectLocalAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Local device connection has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Monitoring", Notes = "Needs per-device indication subscription and disconnect handling.")]
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Per-device monitoring has not been wired into NuraLib yet.");
    }

    public Task StopMonitoringAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
