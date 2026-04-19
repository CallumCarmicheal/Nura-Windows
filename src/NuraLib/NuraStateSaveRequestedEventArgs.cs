namespace NuraLib;

/// <summary>
/// Provides details for a state-save request raised by the library.
/// </summary>
public sealed class NuraStateSaveRequestedEventArgs : EventArgs {
    /// <summary>
    /// Creates a new save-request payload.
    /// </summary>
    /// <param name="reasons">The reasons the host should persist the current state.</param>
    /// <param name="deviceSerial">Optional device serial related to the save request.</param>
    /// <param name="message">Optional descriptive text for diagnostics or logging.</param>
    public NuraStateSaveRequestedEventArgs(
        NuraStateSaveReason reasons,
        string? deviceSerial = null,
        string? message = null) {
        Reasons = reasons;
        DeviceSerial = deviceSerial;
        Message = message;
    }

    /// <summary>
    /// Gets the reasons the state should be persisted.
    /// </summary>
    public NuraStateSaveReason Reasons { get; }

    /// <summary>
    /// Gets the related device serial, when the save request is specific to a device.
    /// </summary>
    public string? DeviceSerial { get; }

    /// <summary>
    /// Gets optional descriptive text for diagnostics or logging.
    /// </summary>
    public string? Message { get; }
}
