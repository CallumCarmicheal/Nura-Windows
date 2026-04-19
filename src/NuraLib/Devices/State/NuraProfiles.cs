using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

/// <summary>
/// Holds the last known profile selection and profile names for a connected device.
/// </summary>
public sealed class NuraProfiles(ConnectedNuraDevice nuraDevice) {
    private int? _current;
    private readonly Dictionary<int, string> _names = [];

    /// <summary>
    /// Raised when the cached active profile identifier changes.
    /// </summary>
    public event EventHandler<NuraValueChangedEventArgs<int?>>? ProfileIdChanged;

    /// <summary>
    /// Gets the last known active profile identifier.
    /// </summary>
    public int? ProfileId => _current;

    /// <summary>
    /// Gets the last known profile-name map keyed by profile identifier.
    /// </summary>
    public IReadOnlyDictionary<int, string> Names => _names;

    internal void UpdateProfileId(int? profileId) {
        var previous = _current;
        _current = profileId;
        if (previous != profileId) {
            ProfileIdChanged?.Invoke(nuraDevice, new NuraValueChangedEventArgs<int?>(previous, profileId));
        }
    }

    internal void UpdateName(int profileId, string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            _names.Remove(profileId);
            return;
        }

        _names[profileId] = name;
    }

    /// <summary>
    /// Actively retrieves the current profile identifier from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<int?> RetrieveProfileIdAsync(CancellationToken cancellationToken = default) {
        return nuraDevice.RetrieveCurrentProfileIdAsync(cancellationToken);
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed current profile update on the headset.")]
    /// <summary>
    /// Sends a request to change the current profile on the headset.
    /// </summary>
    /// <param name="profileId">The target profile identifier.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task SetProfileIdAsync(int profileId, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = profileId;
        throw new NotImplementedException("Bluetooth current profile update has not been wired into NuraLib yet.");
    }

    /// <summary>
    /// Actively retrieves a profile name from the headset.
    /// </summary>
    /// <param name="profileId">The profile identifier to read.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<string?> RetrieveNameAsync(int profileId, CancellationToken cancellationToken = default) {
        return nuraDevice.RetrieveProfileNameAsync(profileId, cancellationToken);
    }

    /// <summary>
    /// Refreshes a contiguous range of profile names and returns the updated cached name map.
    /// </summary>
    /// <param name="profileCount">The number of profile slots to refresh, starting at zero.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task<IReadOnlyDictionary<int, string>> RefreshNamesAsync(int profileCount = 3, CancellationToken cancellationToken = default) {
        return nuraDevice.RefreshProfileNamesAsync(profileCount, cancellationToken);
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed profile name update on the headset.")]
    public Task SetNameAsync(int profileId, string name, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = profileId;
        _ = name ?? throw new ArgumentNullException(nameof(name));
        throw new NotImplementedException("Bluetooth profile name update has not been wired into NuraLib yet.");
    }
}
