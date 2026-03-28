using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

public sealed class NuraDeviceConfiguration {
    private NuraButtonConfiguration? _touchButtons;
    private NuraDialConfiguration? _dial;
    private bool? _headDetectionEnabled;
    private bool? _manualHeadDetectionEnabled;
    private bool? _multipointEnabled;
    private int? _voicePromptGain;

    public NuraButtonConfiguration? TouchButtons => _touchButtons;

    public NuraDialConfiguration? Dial => _dial;

    public bool? HeadDetectionEnabled => _headDetectionEnabled;

    public bool? ManualHeadDetectionEnabled => _manualHeadDetectionEnabled;

    public bool? MultipointEnabled => _multipointEnabled;

    public int? VoicePromptGain => _voicePromptGain;

    internal void UpdateTouchButtons(NuraButtonConfiguration? configuration) {
        _touchButtons = configuration;
    }

    internal void UpdateDial(NuraDialConfiguration? configuration) {
        _dial = configuration;
    }

    internal void UpdateHeadDetectionEnabled(bool? enabled) {
        _headDetectionEnabled = enabled;
    }

    internal void UpdateManualHeadDetectionEnabled(bool? enabled) {
        _manualHeadDetectionEnabled = enabled;
    }

    internal void UpdateMultipointEnabled(bool? enabled) {
        _multipointEnabled = enabled;
    }

    internal void UpdateVoicePromptGain(int? gain) {
        _voicePromptGain = gain;
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed touch button configuration retrieval from the headset.")]
    public Task<NuraButtonConfiguration?> RetrieveTouchButtonsAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth touch button configuration retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed touch button configuration update on the headset.")]
    public Task SetTouchButtonsAsync(NuraButtonConfiguration configuration, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        throw new NotImplementedException("Bluetooth touch button configuration update has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed dial configuration retrieval from the headset.")]
    public Task<NuraDialConfiguration?> RetrieveDialAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth dial configuration retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed dial configuration update on the headset.")]
    public Task SetDialAsync(NuraDialConfiguration configuration, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        throw new NotImplementedException("Bluetooth dial configuration update has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed head detection retrieval from the headset.")]
    public Task<bool?> RetrieveHeadDetectionEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth head detection retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed head detection update on the headset.")]
    public Task SetHeadDetectionEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth head detection update has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed manual head detection retrieval from the headset.")]
    public Task<bool?> RetrieveManualHeadDetectionEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth manual head detection retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed manual head detection update on the headset.")]
    public Task SetManualHeadDetectionEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth manual head detection update has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed multipoint retrieval from the headset.")]
    public Task<bool?> RetrieveMultipointEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth multipoint retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed multipoint update on the headset.")]
    public Task SetMultipointEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth multipoint update has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed voice prompt gain retrieval from the headset.")]
    public Task<int?> RetrieveVoicePromptGainAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth voice prompt gain retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed voice prompt gain update on the headset.")]
    public Task SetVoicePromptGainAsync(int gain, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = gain;
        throw new NotImplementedException("Bluetooth voice prompt gain update has not been wired into NuraLib yet.");
    }
}
