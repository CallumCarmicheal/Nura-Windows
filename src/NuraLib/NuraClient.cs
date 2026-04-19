using NuraLib.Auth;
using NuraLib.Devices;
using NuraLib.Logging;
using NuraLib.Monitoring;

namespace NuraLib;

/// <summary>
/// Entry point for interacting with Nura devices, authentication state, and monitoring.
/// </summary>
public sealed class NuraClient {
    private readonly NuraClientLogger _logger;

    /// <summary>
    /// Creates a new <see cref="NuraClient"/> with the provided state container.
    /// </summary>
    /// <param name="state">
    /// Optional state container used to hold configuration and authentication data.
    /// When omitted, a new empty state object is created.
    /// </param>
    public NuraClient(NuraConfigState? state = null) {
        State = state ?? new NuraConfigState();
        State.StateSaveRequested += HandleStateSaveRequested;
        _logger = new NuraClientLogger(EmitLog);
        Auth = new NuraAuthManager(State, _logger);
        Devices = new NuraDeviceManager(State, _logger);
        Monitoring = new NuraMonitoringManager();
    }

    /// <summary>
    /// Gets the mutable state container used by this client instance.
    /// </summary>
    public NuraConfigState State { get; }

    /// <summary>
    /// Gets the authentication manager for login and session validation operations.
    /// </summary>
    public NuraAuthManager Auth { get; }

    /// <summary>
    /// Gets the device manager used to inspect known and connected devices.
    /// </summary>
    public NuraDeviceManager Devices { get; }

    /// <summary>
    /// Gets the monitoring manager used to subscribe to connection lifecycle events.
    /// </summary>
    public NuraMonitoringManager Monitoring { get; }

    /// <summary>
    /// Raised when the library mutates state that should typically be persisted by the host application.
    /// </summary>
    public event EventHandler<NuraStateSaveRequestedEventArgs>? RequestStateSave;

    /// <summary>
    /// Raised when the library emits a diagnostic log entry.
    /// </summary>
    public event EventHandler<NuraLogEventArgs>? OnLog;

    private void HandleStateSaveRequested(object? sender, NuraStateSaveRequestedEventArgs e) {
        RequestStateSave?.Invoke(this, e);
    }

    private void EmitLog(NuraLogEventArgs e) {
        OnLog?.Invoke(this, e);
    }
}
