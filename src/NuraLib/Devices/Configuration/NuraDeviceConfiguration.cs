using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

/// <summary>
/// Holds the last known configuration values for a connected device.
/// </summary>
public sealed class NuraDeviceConfiguration {
    private NuraButtonConfiguration? _touchButtons;
    private NuraDialConfiguration? _dial;
    private bool? _headDetectionEnabled;
    private bool? _manualHeadDetectionEnabled;
    private bool? _multipointEnabled;
    private NuraVoicePromptGain? _voicePromptGain;

    /// <summary>
    /// Gets the last known touch-button configuration.
    /// </summary>
    public NuraButtonConfiguration? TouchButtons => _touchButtons;

    public NuraDialConfiguration? Dial => _dial;

    public bool? HeadDetectionEnabled => _headDetectionEnabled;

    public bool? ManualHeadDetectionEnabled => _manualHeadDetectionEnabled;

    public bool? MultipointEnabled => _multipointEnabled;

    /// <summary>
    /// Gets the last known voice prompt gain preset.
    /// </summary>
    public NuraVoicePromptGain? VoicePromptGain => _voicePromptGain;

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

    internal void UpdateVoicePromptGain(NuraVoicePromptGain? gain) {
        _voicePromptGain = gain;
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed touch button configuration retrieval from the headset.")]
    /// <summary>
    /// Actively retrieves the touch-button configuration from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<NuraButtonConfiguration?> RetrieveTouchButtonsAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth touch button configuration retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed touch button configuration update on the headset.")]
    /// <summary>
    /// Sends a request to change the touch-button configuration on the headset.
    /// </summary>
    /// <param name="configuration">The desired touch-button configuration.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
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
    /// <summary>
    /// Actively retrieves the current voice prompt gain preset from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<NuraVoicePromptGain?> RetrieveVoicePromptGainAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth voice prompt gain retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Configuration", Notes = "Needs transport-backed voice prompt gain update on the headset.")]
    /// <summary>
    /// Sends a request to change the voice prompt gain preset on the headset.
    /// </summary>
    /// <param name="gain">The desired voice prompt gain preset.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetVoicePromptGainAsync(NuraVoicePromptGain gain, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = gain;
        throw new NotImplementedException("Bluetooth voice prompt gain update has not been wired into NuraLib yet.");
    }
}
