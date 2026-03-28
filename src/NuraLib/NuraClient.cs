using NuraLib.Auth;
using NuraLib.Devices;
using NuraLib.Monitoring;

namespace NuraLib;

public sealed class NuraClient {
    public NuraClient(NuraConfigState? state = null) {
        State = state ?? new NuraConfigState();
        State.StateSaveRequested += HandleStateSaveRequested;
        Auth = new NuraAuthManager(State);
        Devices = new NuraDeviceManager(State);
        Monitoring = new NuraMonitoringManager();
    }

    public NuraConfigState State { get; }

    public NuraAuthManager Auth { get; }

    public NuraDeviceManager Devices { get; }

    public NuraMonitoringManager Monitoring { get; }

    public event EventHandler<NuraStateSaveRequestedEventArgs>? RequestStateSave;

    private void HandleStateSaveRequested(object? sender, NuraStateSaveRequestedEventArgs e) {
        RequestStateSave?.Invoke(this, e);
    }
}
