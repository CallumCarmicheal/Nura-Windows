using NuraPopupWpf.Infrastructure;

namespace NuraPopupWpf.Models;

public sealed class DeviceModel : ObservableObject {
    private bool _isConnected;
    private bool _socialMode;
    private bool _ancEnabled;
    private bool _euVolumeLimiter;
    private int _immersionIndex;
    private bool _isPersonalised;
    private string _warningText;

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
        bool euVolumeLimiter = false,
        int immersionIndex = 3,
        bool isPersonalised = true,
        string warningText = ""
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
        _immersionIndex = Math.Clamp(immersionIndex, 0, 6);
        _isPersonalised = isPersonalised;
        _warningText = warningText;
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

    public int ImmersionIndex {
        get => _immersionIndex;
        set {
            if (SetProperty(ref _immersionIndex, Math.Clamp(value, 0, 6))) {
                OnPropertyChanged(nameof(CurrentImmersionValue));
            }
        }
    }

    public int CurrentImmersionValue => new[] { -2, -1, 0, 1, 2, 3, 4 }[ImmersionIndex];

    public bool IsPersonalised {
        get => _isPersonalised;
        set => SetProperty(ref _isPersonalised, value);
    }

    public string WarningText {
        get => _warningText;
        set {
            if (SetProperty(ref _warningText, value)) {
                OnPropertyChanged(nameof(HasWarningText));
            }
        }
    }

    public bool HasWarningText => !string.IsNullOrWhiteSpace(WarningText);

    public string SocialModeText => SocialMode ? "On" : "Off";

    public string AncStatusText => AncEnabled ? "Active Noise Cancellation" : "Off";

    public string ConnectionStatusText => IsConnected ? "Connected" : "Disconnected";

    public string DeviceStatusSummary => IsConnected
        ? $"{ConnectionStatusText} • {BatteryLevel}% battery"
        : ConnectionStatusText;

    public override string ToString() => Name;
}
