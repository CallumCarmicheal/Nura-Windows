namespace NuraLib.Devices;

/// <summary>
/// Holds the last known live state values for a connected device.
/// </summary>
public sealed class NuraDeviceState(ConnectedNuraDevice nuraDevice) {
    private NuraAncState? _anc;
    private int? _ancLevel;
    private bool? _globalAncEnabled;
    private bool? _spatialEnabled;
    private NuraPersonalisationMode? _personalisationMode;
    private NuraImmersionLevel? _immersionLevel;
    private NuraImmersionLevel? _effectiveImmersionLevel;
    private bool? _proEqEnabled;
    private NuraProEq? _proEq;

    /// <summary>
    /// Raised when the cached ANC state changes.
    /// </summary>
    public event EventHandler<NuraValueChangedEventArgs<NuraAncState>>? AncChanged;

    /// <summary>
    /// Raised when the cached ANC level changes.
    /// </summary>
    public event EventHandler<NuraValueChangedEventArgs<int?>>? AncLevelChanged;

    /// <summary>
    /// Raised when the cached ANC enabled value changes.
    /// </summary>
    public event EventHandler<NuraValueChangedEventArgs<bool?>>? AncEnabledChanged;

    /// <summary>
    /// Raised when the cached passthrough enabled value changes.
    /// </summary>
    public event EventHandler<NuraValueChangedEventArgs<bool?>>? PassthroughEnabledChanged;

    public event EventHandler<NuraValueChangedEventArgs<bool?>>? GlobalAncEnabledChanged;

    public event EventHandler<NuraValueChangedEventArgs<bool?>>? SpatialEnabledChanged;

    public event EventHandler<NuraValueChangedEventArgs<NuraPersonalisationMode?>>? PersonalisationModeChanged;

    public event EventHandler<NuraValueChangedEventArgs<NuraImmersionLevel?>>? ImmersionLevelChanged;

    public event EventHandler<NuraValueChangedEventArgs<NuraImmersionLevel?>>? EffectiveImmersionLevelChanged;

    public event EventHandler<NuraValueChangedEventArgs<bool?>>? ProEqEnabledChanged;

    public event EventHandler<NuraValueChangedEventArgs<NuraProEq?>>? ProEqChanged;

    /// <summary>
    /// Gets the last known ANC state for the connected device.
    /// </summary>
    public NuraAncState? Anc => _anc;

    /// <summary>
    /// Gets the last known ANC level for the connected device.
    /// </summary>
    public int? AncLevel => _ancLevel;

    /// <summary>
    /// Gets the last known ANC enabled state for the connected device.
    /// </summary>
    public bool? AncEnabled => _anc?.AncEnabled;

    /// <summary>
    /// Gets the last known passthrough enabled state for the connected device.
    /// </summary>
    public bool? PassthroughEnabled => _anc?.PassthroughEnabled;

    public bool? GlobalAncEnabled => _globalAncEnabled;

    public bool? SpatialEnabled => _spatialEnabled;

    public NuraPersonalisationMode? PersonalisationMode => _personalisationMode;

    public NuraImmersionLevel? ImmersionLevel => _immersionLevel;

    public NuraImmersionLevel? EffectiveImmersionLevel => _effectiveImmersionLevel;

    public bool? ProEqEnabled => _proEqEnabled;

    public NuraProEq? ProEq => _proEq;

    internal void UpdateAnc(NuraAncState? state) {
        var previous = _anc;
        _anc = state;
        if (!Equals(previous, state)) {
            AncChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<NuraAncState>(previous, state));
        }

        var previousAncEnabled = previous?.AncEnabled;
        var currentAncEnabled = state?.AncEnabled;
        if (previousAncEnabled != currentAncEnabled) {
            AncEnabledChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<bool?>(previousAncEnabled, currentAncEnabled));
        }

        var previousPassthroughEnabled = previous?.PassthroughEnabled;
        var currentPassthroughEnabled = state?.PassthroughEnabled;
        if (previousPassthroughEnabled != currentPassthroughEnabled) {
            PassthroughEnabledChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<bool?>(previousPassthroughEnabled, currentPassthroughEnabled));
        }
    }

    internal void UpdateAncLevel(int? level) {
        var previous = _ancLevel;
        _ancLevel = level;
        if (previous != level) {
            AncLevelChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<int?>(previous, level));
        }
    }

    internal void UpdateGlobalAncEnabled(bool? enabled) {
        var previous = _globalAncEnabled;
        _globalAncEnabled = enabled;
        if (previous != enabled) {
            GlobalAncEnabledChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<bool?>(previous, enabled));
        }
    }

    internal void UpdateSpatialEnabled(bool? enabled) {
        var previous = _spatialEnabled;
        _spatialEnabled = enabled;
        if (previous != enabled) {
            SpatialEnabledChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<bool?>(previous, enabled));
        }
    }

    internal void UpdatePersonalisationMode(NuraPersonalisationMode? mode) {
        var previous = _personalisationMode;
        _personalisationMode = mode;
        if (previous != mode) {
            PersonalisationModeChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<NuraPersonalisationMode?>(previous, mode));
        }
    }

    internal void UpdateImmersionLevel(NuraImmersionLevel? level) {
        var previous = _immersionLevel;
        _immersionLevel = level;
        if (previous != level) {
            ImmersionLevelChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<NuraImmersionLevel?>(previous, level));
        }
    }

    internal void UpdateImmersionLevelRaw(int? rawLevel) {
        UpdateImmersionLevel(rawLevel.HasValue && NuraImmersionLevelExtensions.TryFromRawIndex(rawLevel.Value, out var level) ? level : null);
    }

    internal void UpdateEffectiveImmersionLevel(NuraImmersionLevel? level) {
        var previous = _effectiveImmersionLevel;
        _effectiveImmersionLevel = level;
        if (previous != level) {
            EffectiveImmersionLevelChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<NuraImmersionLevel?>(previous, level));
        }
    }

    internal void UpdateEffectiveImmersionLevelRaw(int? rawLevel) {
        UpdateEffectiveImmersionLevel(rawLevel.HasValue && NuraImmersionLevelExtensions.TryFromRawIndex(rawLevel.Value, out var level) ? level : null);
    }

    internal void UpdateProEqEnabled(bool? enabled) {
        var previous = _proEqEnabled;
        _proEqEnabled = enabled;
        if (previous != enabled) {
            ProEqEnabledChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<bool?>(previous, enabled));
        }
    }

    internal void UpdateProEq(NuraProEq? proEq) {
        var previous = _proEq;
        _proEq = proEq;
        if (!Equals(previous, proEq)) {
            ProEqChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<NuraProEq?>(previous, proEq));
        }
    }

    private void EnsureAudioCapability(NuraAudioCapabilities capability, string featureName) {
        if (!nuraDevice.Info.Supports(capability)) {
            throw new NotSupportedException($"{featureName} is not supported by device {nuraDevice.Info.TypeName} on firmware {nuraDevice.Info.FirmwareVersion}.");
        }
    }

    /// <summary>
    /// Actively retrieves the current ANC state from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<NuraAncState?> RetrieveAncAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Anc, "ANC");
        return nuraDevice.RetrieveAncStateAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a request to change the ANC state on the headset.
    /// </summary>
    /// <param name="state">The desired ANC state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetAncAsync(NuraAncState state, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Anc, "ANC");
        return nuraDevice.SetAncStateAsync(state, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves whether ANC is currently enabled.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task<bool?> RetrieveAncEnabledAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Anc, "ANC");
        return (await nuraDevice.RetrieveAncStateAsync(cancellationToken))?.AncEnabled;
    }

    /// <summary>
    /// Sends a request to enable or disable ANC.
    /// </summary>
    /// <param name="enabled">The desired ANC enabled state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetAncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Anc, "ANC");
        return nuraDevice.SetAncEnabledAsync(enabled, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves whether passthrough is currently enabled.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task<bool?> RetrievePassthroughEnabledAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Anc, "Passthrough");
        return (await nuraDevice.RetrieveAncStateAsync(cancellationToken))?.PassthroughEnabled;
    }

    /// <summary>
    /// Sends a request to enable or disable passthrough.
    /// </summary>
    /// <param name="enabled">The desired passthrough enabled state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetPassthroughEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Anc, "Passthrough");
        return nuraDevice.SetPassthroughEnabledAsync(enabled, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves the current ANC level from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<int?> RetrieveAncLevelAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.AncLevel, "ANC level");
        return nuraDevice.RetrieveAncLevelAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a request to change the ANC level on the headset.
    /// </summary>
    /// <param name="level">The desired ANC level.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetAncLevelAsync(int level, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.AncLevel, "ANC level");
        return nuraDevice.SetAncLevelAsync(level, cancellationToken);
    }

    public Task<bool?> RetrieveGlobalAncEnabledAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.GlobalAncToggle, "Global ANC");
        return nuraDevice.RetrieveGlobalAncEnabledAsync(cancellationToken);
    }

    public Task SetGlobalAncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.GlobalAncToggle, "Global ANC");
        return nuraDevice.SetGlobalAncEnabledAsync(enabled, cancellationToken);
    }

    public Task<bool?> RetrieveSpatialEnabledAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Spatial, "Spatial audio");
        return nuraDevice.RetrieveSpatialEnabledAsync(cancellationToken);
    }

    public Task SetSpatialEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Spatial, "Spatial audio");
        return nuraDevice.SetSpatialEnabledAsync(enabled, cancellationToken);
    }

    public Task<NuraPersonalisationMode?> RetrievePersonalisationModeAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.PersonalisedMode, "Personalised mode");
        return nuraDevice.RetrievePersonalisationModeAsync(cancellationToken);
    }

    public Task SetPersonalisationModeAsync(NuraPersonalisationMode mode, CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.PersonalisedMode, "Personalised mode");
        return nuraDevice.SetPersonalisationModeAsync(mode, cancellationToken);
    }

    public Task<NuraImmersionLevel?> RetrieveImmersionLevelAsync(CancellationToken cancellationToken = default) {
        EnsureAudioCapability(NuraAudioCapabilities.Immersion, "Immersion level");
        return nuraDevice.RetrieveImmersionLevelAsync(cancellationToken);
    }

    public Task SetImmersionLevelAsync(NuraImmersionLevel level, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAudioCapability(NuraAudioCapabilities.Immersion, "Immersion level");
        if (_personalisationMode == NuraPersonalisationMode.Neutral) {
            throw new InvalidOperationException("Immersion level cannot be changed while personalisation mode is Neutral.");
        }
        return nuraDevice.SetImmersionLevelAsync(level, cancellationToken);
    }

    public async Task<NuraImmersionLevel?> RetrieveEffectiveImmersionLevelAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAudioCapability(NuraAudioCapabilities.Immersion, "Effective immersion level");
        if (_effectiveImmersionLevel is not null) {
            return _effectiveImmersionLevel;
        }

        return await RetrieveImmersionLevelAsync(cancellationToken);
    }

    public Task SetEffectiveImmersionLevelAsync(NuraImmersionLevel level, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAudioCapability(NuraAudioCapabilities.Immersion, "Effective immersion level");
        return SetImmersionLevelAsync(level, cancellationToken);
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ enabled retrieval from the headset.")]
    public Task<bool?> RetrieveProEqEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAudioCapability(NuraAudioCapabilities.ProEq, "ProEQ");
        throw new NotImplementedException("Bluetooth ProEQ enabled retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ enabled update on the headset.")]
    public Task SetProEqEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        EnsureAudioCapability(NuraAudioCapabilities.ProEq, "ProEQ");
        throw new NotImplementedException("Bluetooth ProEQ enabled update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ retrieval from the headset.")]
    public Task<NuraProEq?> RetrieveProEqAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAudioCapability(NuraAudioCapabilities.ProEq, "ProEQ");
        throw new NotImplementedException("Bluetooth ProEQ retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ update on the headset.")]
    public Task SetProEqAsync(NuraProEq? proEq, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = proEq;
        EnsureAudioCapability(NuraAudioCapabilities.ProEq, "ProEQ");
        throw new NotImplementedException("Bluetooth ProEQ update has not been wired into NuraLib yet.");
    }
}
