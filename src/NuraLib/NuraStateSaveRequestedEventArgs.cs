namespace NuraLib;

public sealed class NuraStateSaveRequestedEventArgs : EventArgs {
    public NuraStateSaveRequestedEventArgs(
        NuraStateSaveReason reasons,
        string? deviceSerial = null,
        string? message = null) {
        Reasons = reasons;
        DeviceSerial = deviceSerial;
        Message = message;
    }

    public NuraStateSaveReason Reasons { get; }

    public string? DeviceSerial { get; }

    public string? Message { get; }
}
