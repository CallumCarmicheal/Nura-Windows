using NuraLib.Devices;
using NuraPopupWpf.Infrastructure;

namespace NuraPopupWpf.Models;

public class DeviceModel : ObservableObject {
    private string _name;
    private bool _isConnected;
    private bool _socialMode;
    private bool _ancEnabled;
    private bool _euVolumeLimiter;
    private int _immersionIndex;
    private bool _isPersonalised;
    private string _warningText;
    private int? _batteryLevel;
    private string _serialNumber;
    private string _softwareVersion;
    private IReadOnlyList<ProfileModel> _profiles;
    private bool _spatialEnabled;
    private int? _ancLevel;
    private bool _supportsAnc;
    private bool _supportsAncLevel;
    private bool _supportsSpatial;
    private bool _supportsTouchButtons;
    private bool _supportsDial;
    private bool _supportsEuVolumeLimiter;
    private NuraButtonConfiguration? _touchButtons;
    private NuraDialConfiguration? _dial;

    public DeviceModel(
        string id,
        string name,
        int? batteryLevel,
        string serialNumber,
        string softwareVersion,
        IReadOnlyList<ProfileModel> profiles,
        bool isConnected = true,
        bool socialMode = false,
        bool ancEnabled = true,
        bool euVolumeLimiter = false,
        int immersionIndex = 3,
        bool isPersonalised = true,
        string warningText = "",
        ConnectedNuraDevice? liveDevice = null
    ) {
        Id = id;
        _name = name;
        LiveDevice = liveDevice;
        _batteryLevel = batteryLevel;
        _serialNumber = serialNumber;
        _softwareVersion = softwareVersion;
        _profiles = profiles;
        _isConnected = isConnected;
        _socialMode = socialMode;
        _ancEnabled = ancEnabled;
        _euVolumeLimiter = euVolumeLimiter;
        _immersionIndex = Math.Clamp(immersionIndex, 0, 6);
        _isPersonalised = isPersonalised;
        _warningText = warningText;
        _supportsEuVolumeLimiter = euVolumeLimiter;
    }

    public string Id { get; }

    public string Name {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public ConnectedNuraDevice? LiveDevice { get; private set; }

    public bool IsLive => LiveDevice is not null;

    public int? BatteryLevel {
        get => _batteryLevel;
        set {
            if (SetProperty(ref _batteryLevel, value)) {
                OnPropertyChanged(nameof(BatteryText));
                OnPropertyChanged(nameof(DeviceStatusSummary));
            }
        }
    }

    public string SerialNumber {
        get => _serialNumber;
        set => SetProperty(ref _serialNumber, value);
    }

    public string SoftwareVersion {
        get => _softwareVersion;
        set => SetProperty(ref _softwareVersion, value);
    }

    public IReadOnlyList<ProfileModel> Profiles {
        get => _profiles;
        set => SetProperty(ref _profiles, value);
    }

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

    public bool SupportsAnc {
        get => _supportsAnc;
        set => SetProperty(ref _supportsAnc, value);
    }

    public bool SupportsAncLevel {
        get => _supportsAncLevel;
        set => SetProperty(ref _supportsAncLevel, value);
    }

    public bool SupportsSpatial {
        get => _supportsSpatial;
        set => SetProperty(ref _supportsSpatial, value);
    }

    public bool SupportsTouchButtons {
        get => _supportsTouchButtons;
        set => SetProperty(ref _supportsTouchButtons, value);
    }

    public bool SupportsDial {
        get => _supportsDial;
        set => SetProperty(ref _supportsDial, value);
    }

    public bool SupportsEuVolumeLimiter {
        get => _supportsEuVolumeLimiter;
        set => SetProperty(ref _supportsEuVolumeLimiter, value);
    }

    public bool SpatialEnabled {
        get => _spatialEnabled;
        set => SetProperty(ref _spatialEnabled, value);
    }

    public int? AncLevel {
        get => _ancLevel;
        set => SetProperty(ref _ancLevel, value);
    }

    public NuraButtonConfiguration? TouchButtons {
        get => _touchButtons;
        set => SetProperty(ref _touchButtons, value);
    }

    public NuraDialConfiguration? Dial {
        get => _dial;
        set => SetProperty(ref _dial, value);
    }

    public bool HasWarningText => !string.IsNullOrWhiteSpace(WarningText);

    public string SocialModeText => SocialMode ? "On" : "Off";

    public string AncStatusText => AncEnabled ? "Active Noise Cancellation" : "Off";

    public string ConnectionStatusText => IsConnected ? "Connected" : "Disconnected";

    public string BatteryText => BatteryLevel is > -1 ? $"{BatteryLevel.Value}%" : "N/A";

    public string DeviceStatusSummary => IsConnected
        ? BatteryLevel is > -1
            ? $"{ConnectionStatusText} • {BatteryText} battery"
            : ConnectionStatusText
        : ConnectionStatusText;

    public virtual void AttachLiveDevice(ConnectedNuraDevice? liveDevice) {
        LiveDevice = liveDevice;
        OnPropertyChanged(nameof(IsLive));
    }

    public override string ToString() => Name;
}
