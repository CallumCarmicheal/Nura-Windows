using NuraLib.Configuration;

namespace NuraLib;

/// <summary>
/// Holds the current in-memory configuration and provides save-request notifications for the host application.
/// </summary>
public sealed class NuraConfigState {
    /// <summary>
    /// Creates a new state container.
    /// </summary>
    /// <param name="configuration">
    /// Optional initial configuration to use. When omitted, a new empty configuration is created.
    /// </param>
    public NuraConfigState(NuraConfig? configuration = null) {
        Configuration = configuration ?? new NuraConfig();
    }

    /// <summary>
    /// Gets the current library configuration snapshot.
    /// </summary>
    public NuraConfig Configuration { get; private set; }

    internal event EventHandler<NuraStateSaveRequestedEventArgs>? StateSaveRequested;

    /// <summary>
    /// Replaces the current configuration object and requests that the host application persist it.
    /// </summary>
    /// <param name="configuration">The new configuration instance.</param>
    /// <param name="reason">The primary reason the state should be persisted.</param>
    /// <param name="message">Optional descriptive text for diagnostics or logging.</param>
    public void ReplaceConfiguration(
        NuraConfig configuration,
        NuraStateSaveReason reason = NuraStateSaveReason.Configuration,
        string? message = null) {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        RequestSave(reason, message: message);
    }

    /// <summary>
    /// Requests that the host application persist the current state.
    /// </summary>
    /// <param name="reason">The reason or reasons the state should be saved.</param>
    /// <param name="deviceSerial">Optional device serial related to the save request.</param>
    /// <param name="message">Optional descriptive text for diagnostics or logging.</param>
    public void RequestSave(
        NuraStateSaveReason reason,
        string? deviceSerial = null,
        string? message = null) {
        StateSaveRequested?.Invoke(
            this,
            new NuraStateSaveRequestedEventArgs(reason, deviceSerial, message));
    }
}
