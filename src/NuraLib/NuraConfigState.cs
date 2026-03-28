using NuraLib.Configuration;

namespace NuraLib;

public sealed class NuraConfigState {
    public NuraConfigState(NuraConfig? configuration = null) {
        Configuration = configuration ?? new NuraConfig();
    }

    public NuraConfig Configuration { get; private set; }

    internal event EventHandler<NuraStateSaveRequestedEventArgs>? StateSaveRequested;

    public void ReplaceConfiguration(
        NuraConfig configuration,
        NuraStateSaveReason reason = NuraStateSaveReason.Configuration,
        string? message = null) {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        RequestSave(reason, message: message);
    }

    public void RequestSave(
        NuraStateSaveReason reason,
        string? deviceSerial = null,
        string? message = null) {
        StateSaveRequested?.Invoke(
            this,
            new NuraStateSaveRequestedEventArgs(reason, deviceSerial, message));
    }
}
