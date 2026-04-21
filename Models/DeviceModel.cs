using NuraPopupWpf.Infrastructure;

namespace NuraPopupWpf.Models;

public sealed class DeviceModel : ObservableObject {
    private bool _isConnected;
    private bool _socialMode;
    private bool _ancEnabled;
    private bool _euVolumeLimiter;

    public DeviceModel(
        string id,
        string name,
        int batteryLevel,
        string serialNumber,
        string softwareVersion,
        IReadOnlyList<ProfileModel> profiles,
        bool isConnected = true,
        bool socialMode = false,
        bool ancEnabled = true,
        bool euVolumeLimiter = false
    ) {
        Id = id;
        Name = name;
        BatteryLevel = batteryLevel;
        SerialNumber = serialNumber;
        SoftwareVersion = softwareVersion;
        Profiles = profiles;
        _isConnected = isConnected;
        _socialMode = socialMode;
        _ancEnabled = ancEnabled;
        _euVolumeLimiter = euVolumeLimiter;
    }

    public string Id { get; }

    public string Name { get; }

    public int BatteryLevel { get; }

    public string SerialNumber { get; }

    public string SoftwareVersion { get; }

    public IReadOnlyList<ProfileModel> Profiles { get; }

    public bool IsConnected {
        get => _isConnected;
        set {
            if (SetProperty(ref _isConnected, value)) {
                OnPropertyChanged(nameof(ConnectionStatusText));
                OnPropertyChanged(nameof(DeviceStatusSummary));
            }
        }
    }

    public bool SocialMode {
        get => _socialMode;
        set {
            if (SetProperty(ref _socialMode, value)) {
                OnPropertyChanged(nameof(SocialModeText));
            }
        }
    }

    public bool AncEnabled {
        get => _ancEnabled;
        set {
            if (SetProperty(ref _ancEnabled, value)) {
                OnPropertyChanged(nameof(AncStatusText));
            }
        }
    }

    public bool EuVolumeLimiter {
        get => _euVolumeLimiter;
        set => SetProperty(ref _euVolumeLimiter, value);
    }

    public string SocialModeText => SocialMode ? "On" : "Off";

    public string AncStatusText => AncEnabled ? "Active Noise Cancellation" : "Off";

    public string ConnectionStatusText => IsConnected ? "Connected" : "Disconnected";

    public string DeviceStatusSummary => IsConnected
        ? $"{ConnectionStatusText} • {BatteryLevel}% battery"
        : ConnectionStatusText;

    public override string ToString() => Name;
}
