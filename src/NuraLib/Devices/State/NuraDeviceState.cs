namespace NuraLib.Devices;

/// <summary>
/// Holds the last known live state values for a connected device.
/// </summary>
public sealed class NuraDeviceState(ConnectedNuraDevice nuraDevice) {
    private NuraAncState? _anc;
    private int? _ancLevel;
    private bool? _globalAncEnabled;
    private bool? _spatialEnabled;
    private bool? _immersionEnabled;
    private int? _immersionLevel;
    private int? _effectiveImmersionLevel;
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

    public event EventHandler<NuraValueChangedEventArgs<bool?>>? ImmersionEnabledChanged;

    public event EventHandler<NuraValueChangedEventArgs<int?>>? ImmersionLevelChanged;

    public event EventHandler<NuraValueChangedEventArgs<int?>>? EffectiveImmersionLevelChanged;

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

    public bool? ImmersionEnabled => _immersionEnabled;

    public int? ImmersionLevel => _immersionLevel;

    public int? EffectiveImmersionLevel => _effectiveImmersionLevel;

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

    internal void UpdateImmersionEnabled(bool? enabled) {
        var previous = _immersionEnabled;
        _immersionEnabled = enabled;
        if (previous != enabled) {
            ImmersionEnabledChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<bool?>(previous, enabled));
        }
    }

    internal void UpdateImmersionLevel(int? level) {
        var previous = _immersionLevel;
        _immersionLevel = level;
        if (previous != level) {
            ImmersionLevelChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<int?>(previous, level));
        }
    }

    internal void UpdateEffectiveImmersionLevel(int? level) {
        var previous = _effectiveImmersionLevel;
        _effectiveImmersionLevel = level;
        if (previous != level) {
            EffectiveImmersionLevelChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<int?>(previous, level));
        }
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

    /// <summary>
    /// Actively retrieves the current ANC state from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<NuraAncState?> RetrieveAncAsync(CancellationToken cancellationToken = default) {
        return nuraDevice.RetrieveAncStateAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a request to change the ANC state on the headset.
    /// </summary>
    /// <param name="state">The desired ANC state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetAncAsync(NuraAncState state, CancellationToken cancellationToken = default) {
        return nuraDevice.SetAncStateAsync(state, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves whether ANC is currently enabled.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task<bool?> RetrieveAncEnabledAsync(CancellationToken cancellationToken = default) {
        return (await nuraDevice.RetrieveAncStateAsync(cancellationToken))?.AncEnabled;
    }

    /// <summary>
    /// Sends a request to enable or disable ANC.
    /// </summary>
    /// <param name="enabled">The desired ANC enabled state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetAncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        return nuraDevice.SetAncEnabledAsync(enabled, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves whether passthrough is currently enabled.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task<bool?> RetrievePassthroughEnabledAsync(CancellationToken cancellationToken = default) {
        return (await nuraDevice.RetrieveAncStateAsync(cancellationToken))?.PassthroughEnabled;
    }

    /// <summary>
    /// Sends a request to enable or disable passthrough.
    /// </summary>
    /// <param name="enabled">The desired passthrough enabled state.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetPassthroughEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        return nuraDevice.SetPassthroughEnabledAsync(enabled, cancellationToken);
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ANC level retrieval from the headset.")]
    /// <summary>
    /// Actively retrieves the current ANC level from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<int?> RetrieveAncLevelAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth ANC level retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ANC level update on the headset.")]
    /// <summary>
    /// Sends a request to change the ANC level on the headset.
    /// </summary>
    /// <param name="level">The desired ANC level.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetAncLevelAsync(int level, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = level;
        throw new NotImplementedException("Bluetooth ANC level update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed global ANC retrieval from the headset.")]
    public Task<bool?> RetrieveGlobalAncEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth global ANC retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed global ANC update on the headset.")]
    public Task SetGlobalAncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth global ANC update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed spatial state retrieval from the headset.")]
    public Task<bool?> RetrieveSpatialEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth spatial state retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed spatial state update on the headset.")]
    public Task SetSpatialEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth spatial state update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed immersion state retrieval from the headset.")]
    public Task<bool?> RetrieveImmersionEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth immersion state retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed immersion state update on the headset.")]
    public Task SetImmersionEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth immersion state update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed immersion level retrieval from the headset.")]
    public Task<int?> RetrieveImmersionLevelAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth immersion level retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed immersion level update on the headset.")]
    public Task SetImmersionLevelAsync(int level, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = level;
        throw new NotImplementedException("Bluetooth immersion level update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed effective immersion level retrieval from the headset.")]
    public Task<int?> RetrieveEffectiveImmersionLevelAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth effective immersion level retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed effective immersion level update on the headset.")]
    public Task SetEffectiveImmersionLevelAsync(int level, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = level;
        throw new NotImplementedException("Bluetooth effective immersion level update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ enabled retrieval from the headset.")]
    public Task<bool?> RetrieveProEqEnabledAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth ProEQ enabled retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ enabled update on the headset.")]
    public Task SetProEqEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = enabled;
        throw new NotImplementedException("Bluetooth ProEQ enabled update has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ retrieval from the headset.")]
    public Task<NuraProEq?> RetrieveProEqAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth ProEQ retrieval has not been wired into NuraLib yet.");
    }

    [Utilities.Docs.BluetoothImplementationRequired("State", Notes = "Needs transport-backed ProEQ update on the headset.")]
    public Task SetProEqAsync(NuraProEq? proEq, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = proEq;
        throw new NotImplementedException("Bluetooth ProEQ update has not been wired into NuraLib yet.");
    }
}
