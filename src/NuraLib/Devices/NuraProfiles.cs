using NuraLib.Utilities.Docs;

namespace NuraLib.Devices;

public sealed class NuraProfiles {
    private int? _current;
    private readonly Dictionary<int, string> _names = [];

    public int? ProfileId => _current;

    public IReadOnlyDictionary<int, string> Names => _names;

    internal void UpdateProfileId(int? profileId) {
        _current = profileId;
    }

    internal void UpdateName(int profileId, string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            _names.Remove(profileId);
            return;
        }

        _names[profileId] = name;
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed current profile retrieval from the headset.")]
    public Task<int?> RetrieveProfileIdAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Bluetooth current profile retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed current profile update on the headset.")]
    public Task SetProfileIdAsync(int profileId, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = profileId;
        throw new NotImplementedException("Bluetooth current profile update has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed profile name retrieval from the headset.")]
    public Task<string?> RetrieveNameAsync(int profileId, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = profileId;
        throw new NotImplementedException("Bluetooth profile name retrieval has not been wired into NuraLib yet.");
    }

    [BluetoothImplementationRequired("Profiles", Notes = "Needs transport-backed profile name update on the headset.")]
    public Task SetNameAsync(int profileId, string name, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        _ = profileId;
        _ = name ?? throw new ArgumentNullException(nameof(name));
        throw new NotImplementedException("Bluetooth profile name update has not been wired into NuraLib yet.");
    }
}
