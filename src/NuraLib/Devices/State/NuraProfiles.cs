using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

/// <summary>
/// Holds the last known profile selection and profile names for a connected device.
/// </summary>
public sealed class NuraProfiles(ConnectedNuraDevice nuraDevice) {
    private int? _current;
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<int, NuraProfileVisualisationData> _visualisations = [];
    private NuraProfileVisualisationData? _currentVisualisation;

    /// <summary>
    /// Raised when the cached active profile identifier changes.
    /// </summary>
    public event EventHandler<NuraValueChangedEventArgs<int?>>? ProfileIdChanged;

    /// <summary>
    /// Raised when one or more cached profile names change.
    /// </summary>
    public event EventHandler? NamesChanged;

    /// <summary>
    /// Raised when one or more cached profile visualisations change.
    /// </summary>
    public event EventHandler? VisualisationsChanged;

    /// <summary>
    /// Gets the last known active profile identifier.
    /// </summary>
    public int? ProfileId => _current;

    /// <summary>
    /// Gets the last known profile-name map keyed by profile identifier.
    /// </summary>
    public IReadOnlyDictionary<int, string> Names => _names;

    /// <summary>
    /// Gets the cached profile visualisations keyed by profile identifier.
    /// </summary>
    public IReadOnlyDictionary<int, NuraProfileVisualisationData> Visualisations => _visualisations;

    /// <summary>
    /// Gets the last known current-profile visualisation data.
    /// </summary>
    public NuraProfileVisualisationData? CurrentVisualisation => _currentVisualisation;

    internal void UpdateProfileId(int? profileId) {
        var previous = _current;
        _current = profileId;
        if (previous != profileId) {
            ProfileIdChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<int?>(previous, profileId));
            nuraDevice.RaiseChanged();
        }
    }

    internal void UpdateName(int profileId, string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            if (_names.Remove(profileId)) {
                NamesChanged?.Invoke(nuraDevice, EventArgs.Empty);
                nuraDevice.RaiseChanged();
            }
            return;
        }

        if (_names.TryGetValue(profileId, out var existing) && string.Equals(existing, name, StringComparison.Ordinal)) {
            return;
        }

        _names[profileId] = name;
        NamesChanged?.Invoke(nuraDevice, EventArgs.Empty);
        nuraDevice.RaiseChanged();
    }

    internal void UpdateCurrentVisualisation(NuraProfileVisualisationData? visualisation) {
        if (Equals(_currentVisualisation, visualisation)) {
            return;
        }

        _currentVisualisation = visualisation;
        if (_current is { } currentProfileId && visualisation is { Valid: true }) {
            UpdateVisualisation(currentProfileId, visualisation);
            return;
        }

        VisualisationsChanged?.Invoke(nuraDevice, EventArgs.Empty);
        nuraDevice.RaiseChanged();
    }

    internal void UpdateVisualisation(int profileId, NuraProfileVisualisationData? visualisation) {
        if (visualisation is null || !visualisation.Valid) {
            if (_visualisations.Remove(profileId)) {
                VisualisationsChanged?.Invoke(nuraDevice, EventArgs.Empty);
                nuraDevice.RaiseChanged();
            }
            return;
        }

        if (_visualisations.TryGetValue(profileId, out var existing) && Equals(existing, visualisation)) {
            return;
        }

        _visualisations[profileId] = visualisation;
        VisualisationsChanged?.Invoke(nuraDevice, EventArgs.Empty);
        nuraDevice.RaiseChanged();
    }

    internal void ApplyOnlineProfileMetadata(IReadOnlyList<Auth.NuraAuthProfileVisualisationSlot> slots) {
        var namesChanged = false;
        var visualsChanged = false;

        foreach (var slot in slots) {
            if (!string.IsNullOrWhiteSpace(slot.Name)) {
                if (!_names.TryGetValue(slot.ProfileId, out var existingName) || !string.Equals(existingName, slot.Name, StringComparison.Ordinal)) {
                    _names[slot.ProfileId] = slot.Name!;
                    namesChanged = true;
                }
            }

            if (slot.Visualisation is { Valid: true }) {
                if (!_visualisations.TryGetValue(slot.ProfileId, out var existingVisual) || !Equals(existingVisual, slot.Visualisation)) {
                    _visualisations[slot.ProfileId] = slot.Visualisation;
                    visualsChanged = true;
                }
            }
        }

        if (namesChanged) {
            NamesChanged?.Invoke(nuraDevice, EventArgs.Empty);
        }

        if (visualsChanged) {
            VisualisationsChanged?.Invoke(nuraDevice, EventArgs.Empty);
        }

        if (namesChanged || visualsChanged) {
            nuraDevice.RaiseChanged();
        }
    }

    private void EnsureProfilesSupported() {
        if (!nuraDevice.Info.Supports(NuraSystemCapabilities.Profiles)) {
            throw new NotSupportedException($"Profiles are not supported by device {nuraDevice.Info.TypeName} on firmware {nuraDevice.Info.FirmwareVersion}.");
        }
    }

    private void EnsureVisualisationSupported() {
        if (!nuraDevice.Info.Supports(NuraAudioCapabilities.VisualisationData)) {
            throw new NotSupportedException($"Profile visualisation data is not supported by device {nuraDevice.Info.TypeName} on firmware {nuraDevice.Info.FirmwareVersion}.");
        }
    }

    /// <summary>
    /// Actively retrieves the current profile identifier from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<int?> RetrieveProfileIdAsync(CancellationToken cancellationToken = default) {
        EnsureProfilesSupported();
        return nuraDevice.RetrieveCurrentProfileIdAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a request to change the current profile on the headset.
    /// </summary>
    /// <param name="profileId">The target profile identifier.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetProfileIdAsync(int profileId, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProfilesSupported();
        return nuraDevice.SetCurrentProfileIdAsync(profileId, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves a profile name from the headset.
    /// </summary>
    /// <param name="profileId">The profile identifier to read.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<string?> RetrieveNameAsync(int profileId, CancellationToken cancellationToken = default) {
        EnsureProfilesSupported();
        return nuraDevice.RetrieveProfileNameAsync(profileId, cancellationToken);
    }

    /// <summary>
    /// Refreshes a contiguous range of profile names and returns the updated cached name map.
    /// </summary>
    /// <param name="profileCount">The number of profile slots to refresh, starting at zero.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<IReadOnlyDictionary<int, string>> RefreshNamesAsync(int profileCount = 3, CancellationToken cancellationToken = default) {
        EnsureProfilesSupported();
        return nuraDevice.RefreshProfileNamesAsync(profileCount, cancellationToken);
    }

    /// <summary>
    /// Actively retrieves the current profile visualisation from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<NuraProfileVisualisationData?> RetrieveCurrentVisualisationAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProfilesSupported();
        EnsureVisualisationSupported();
        return nuraDevice.RetrieveCurrentVisualisationAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes cached profile visualisation data from the authenticated backend and the active headset.
    /// </summary>
    /// <param name="profileCount">The number of profile slots to refresh names for before applying visual metadata.</param>
    /// <param name="includeOnlineMetadata">When <see langword="true"/>, also queries the authenticated backend bootstrap flow for bulk profile metadata.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task<IReadOnlyDictionary<int, NuraProfileVisualisationData>> RefreshVisualisationsAsync(
        int profileCount = 3,
        bool includeOnlineMetadata = true,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProfilesSupported();
        EnsureVisualisationSupported();

        await nuraDevice.RefreshProfileNamesAsync(profileCount, cancellationToken);
        if (includeOnlineMetadata) {
            await nuraDevice.RefreshOnlineProfileMetadataAsync(cancellationToken);
        }

        await nuraDevice.RefreshProfileVisualisationsAsync(profileCount, cancellationToken);
        return Visualisations;
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed profile name update on the headset.")]
    public Task SetNameAsync(int profileId, string name, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProfilesSupported();
        _ = profileId;
        _ = name ?? throw new ArgumentNullException(nameof(name));
        throw new NotImplementedException("Bluetooth profile name update has not been wired into NuraLib yet.");
    }
}
