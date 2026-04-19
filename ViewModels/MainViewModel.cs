using Microsoft.VisualBasic.FileIO;

using NuraPopupWpf.Infrastructure;
using NuraPopupWpf.Models;
using NuraPopupWpf.Services;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NuraPopupWpf.ViewModels;

public sealed class MainViewModel : ObservableObject {
    private const int MinExportRenderSize = 4;
    private const int MaxExportRenderSize = 12288;

    private readonly NuraProfileRenderer _renderer = new();
    private readonly HearingProfileExportService _profileExportService = new();
    private readonly ObservableCollection<string> _modes = new ObservableCollection<string>(new[] { "Neutral", "Personalised" });
    private readonly Stopwatch _animationStopwatch = new();

    private TimeSpan _animationDuration = TimeSpan.FromMilliseconds(420);
    private ProfileModel? _animationFromProfile;
    private ProfileModel? _animationToProfile;
    private double _animationFromMode;
    private double _animationToMode;
    private bool _animateProfileBlend;
    private bool _isAnimationRunning;

    private bool _isExpanded;
    private bool _isDevicePage = true;
    private DeviceModel _currentDevice = null!;
    private ProfileModel _currentProfile = null!;
    private ProfileModel _displayedProfile = null!;
    private double _displayedModeProgress = 1.0;
    private ProfileModel _visualFromProfile = null!;
    private ProfileModel _visualToProfile = null!;
    private double _visualProfileBlendProgress = 1.0;
    private double _visualModeProgress = 1.0;
    private int _immersionIndex = 3;
    private bool _isPersonalised = true;
    private bool _socialMode;
    private bool _ancEnabled = true;
    private bool _euVolumeLimiter;
    private bool _isSerialVisible;
    private bool _isCompactProfileSelectorOpen;
    private bool _useBitmapProfileRenderer;
    private bool _isProfileMorphing;
    private string _exportRenderSizeText = "1024";
    private string _exportStatusText = "Save transparent PNG renders for every profile on every available device.";

    public MainViewModel() {
        Profiles = BuildProfiles();
        Devices = new ObservableCollection<DeviceModel>(BuildDevices());

        foreach (var profile in Profiles.Values) {
            profile.Thumbnail = _renderer.RenderThumbnail(profile, 20);
        }

        _currentDevice = Devices[0];
        _currentProfile = _currentDevice.Profiles[0];
        _displayedProfile = _currentProfile;
        _displayedModeProgress = 1.0;
        _visualFromProfile = _currentProfile;
        _visualToProfile = _currentProfile;
        _visualProfileBlendProgress = 1.0;
        _visualModeProgress = 1.0;

        ToggleExpandedCommand = new RelayCommand(_ => IsExpanded = !IsExpanded);
        ShowDevicePageCommand = new RelayCommand(_ => IsDevicePage = true);
        ShowSettingsPageCommand = new RelayCommand(_ => IsDevicePage = false);
        SelectModeCommand = new RelayCommand(parameter => {
            if (parameter is string mode) {
                SelectedMode = mode;
            }
        });
        ToggleSerialVisibilityCommand = new RelayCommand(_ => {
            IsSerialVisible = !IsSerialVisible;
        });
        ExportHearingProfilesCommand = new RelayCommand(_ => {
            ExportHearingProfiles();
        });
    }

    public ObservableCollection<DeviceModel> Devices { get; }

    public IReadOnlyDictionary<string, ProfileModel> Profiles { get; }

    public ObservableCollection<string> Modes => _modes;

    public ICommand ToggleExpandedCommand { get; }

    public ICommand ShowDevicePageCommand { get; }

    public ICommand ShowSettingsPageCommand { get; }

    public ICommand SelectModeCommand { get; }

    public ICommand ToggleSerialVisibilityCommand { get; }

    public ICommand ExportHearingProfilesCommand { get; }

    public bool IsExpanded {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsDevicePage {
        get => _isDevicePage;
        set {
            if (SetProperty(ref _isDevicePage, value)) {
                OnPropertyChanged(nameof(IsSettingsPage));
            }
        }
    }

    public bool IsSettingsPage => !IsDevicePage;

    public DeviceModel CurrentDevice {
        get => _currentDevice;
        set {
            if (!SetProperty(ref _currentDevice, value)) {
                return;
            }

            OnPropertyChanged(nameof(CurrentProfiles));
            OnPropertyChanged(nameof(CurrentProfileCount));
            OnPropertyChanged(nameof(CurrentBatteryText));
            OnPropertyChanged(nameof(DisplaySerial));
            OnPropertyChanged(nameof(CurrentSoftwareVersion));
            IsCompactProfileSelectorOpen = false;

            if (!_currentDevice.Profiles.Contains(_currentProfile)) {
                CurrentProfile = _currentDevice.Profiles[0];
            } else {
                UpdateProfileImage();
            }
        }
    }

    public IReadOnlyList<ProfileModel> CurrentProfiles => CurrentDevice.Profiles;

    public ProfileModel CurrentProfile {
        get => _currentProfile;
        set {
            if (!SetProperty(ref _currentProfile, value)) {
                return;
            }

            IsCompactProfileSelectorOpen = false;
            StartProfileAnimation(profileChanged: true, modeChanged: false);
        }
    }

    public string SelectedMode {
        get => IsPersonalised ? "Personalised" : "Neutral";
        set => IsPersonalised = value == "Personalised";
    }

    public bool IsPersonalised {
        get => _isPersonalised;
        set {
            if (!SetProperty(ref _isPersonalised, value)) {
                return;
            }

            OnPropertyChanged(nameof(SelectedMode));
            StartProfileAnimation(profileChanged: false, modeChanged: true);
        }
    }

    public int ImmersionIndex {
        get => _immersionIndex;
        set {
            if (!SetProperty(ref _immersionIndex, value)) {
                return;
            }

            OnPropertyChanged(nameof(CurrentImmersionValue));
            UpdateProfileImage();
        }
    }

    public int CurrentImmersionValue => new[] { -2, -1, 0, 1, 2, 3, 4 }[ImmersionIndex];

    public bool SocialMode {
        get => _socialMode;
        set {
            if (SetProperty(ref _socialMode, value)) {
                OnPropertyChanged(nameof(SocialModeText));
            }
        }
    }

    public string SocialModeText => SocialMode ? "On" : "Off";

    public bool AncEnabled {
        get => _ancEnabled;
        set {
            if (SetProperty(ref _ancEnabled, value)) {
                OnPropertyChanged(nameof(AncStatusText));
            }
        }
    }

    public string AncStatusText => AncEnabled ? "Active Noise Cancellation" : "Off";

    public bool EuVolumeLimiter {
        get => _euVolumeLimiter;
        set => SetProperty(ref _euVolumeLimiter, value);
    }

    public bool IsSerialVisible {
        get => _isSerialVisible;
        set {
            if (SetProperty(ref _isSerialVisible, value)) {
                OnPropertyChanged(nameof(DisplaySerial));
                OnPropertyChanged(nameof(SerialButtonText));
            }
        }
    }

    public bool IsCompactProfileSelectorOpen {
        get => _isCompactProfileSelectorOpen;
        set => SetProperty(ref _isCompactProfileSelectorOpen, value);
    }

    public bool UseBitmapProfileRenderer {
        get => _useBitmapProfileRenderer;
        set {
            if (SetProperty(ref _useBitmapProfileRenderer, value)) {
                OnPropertyChanged(nameof(ActiveProfileRendererLabel));
                OnPropertyChanged(nameof(ProfileRendererSubtitle));
            }
        }
    }

    public string ActiveProfileRendererLabel => UseBitmapProfileRenderer ? "Bitmap renderer" : "Shape renderer";

    public string ProfileRendererSubtitle => UseBitmapProfileRenderer
        ? "Use the nura renderer (slower)."
        : "Use the Shape renderer (faster but not as accurate).";

    public string ExportRenderSizeText {
        get => _exportRenderSizeText;
        set => SetProperty(ref _exportRenderSizeText, value);
    }

    public string ExportStatusText {
        get => _exportStatusText;
        private set => SetProperty(ref _exportStatusText, value);
    }

    public bool IsProfileMorphing {
        get => _isProfileMorphing;
        private set => SetProperty(ref _isProfileMorphing, value);
    }

    public string DisplaySerial {
        get {
            var serial = CurrentDevice.SerialNumber;
            if (IsSerialVisible) {
                return serial;
            }

            return string.Concat(serial.Take(3)) + new string('•', Math.Max(0, serial.Length - 3));
        }
    }

    public string SerialButtonText => IsSerialVisible ? "Hide" : "Show";

    public string CurrentBatteryText => $"{CurrentDevice.BatteryLevel}%";

    public string CurrentSoftwareVersion => CurrentDevice.SoftwareVersion;

    public int CurrentProfileCount => CurrentProfiles.Count;

    public ProfileModel VisualFromProfile {
        get => _visualFromProfile;
        private set => SetProperty(ref _visualFromProfile, value);
    }

    public ProfileModel VisualToProfile {
        get => _visualToProfile;
        private set => SetProperty(ref _visualToProfile, value);
    }

    public double VisualProfileBlendProgress {
        get => _visualProfileBlendProgress;
        private set => SetProperty(ref _visualProfileBlendProgress, value);
    }

    public double VisualModeProgress {
        get => _visualModeProgress;
        private set => SetProperty(ref _visualModeProgress, value);
    }

    private void StartProfileAnimation(bool profileChanged, bool modeChanged) {
        var visualProfile = CaptureCurrentVisualProfile();
        var visualModeProgress = _visualModeProgress;

        StopAnimationLoop();
        _animationFromProfile = visualProfile;
        _animationToProfile = _currentProfile;
        _animationFromMode = visualModeProgress;
        _animationToMode = _isPersonalised ? 1.0 : 0.0;
        _animateProfileBlend = profileChanged && !ReferenceEquals(visualProfile, _currentProfile);
        IsProfileMorphing = _animateProfileBlend;
        _animationDuration = TimeSpan.FromMilliseconds(profileChanged ? 520 : 420);

        if (!profileChanged && !modeChanged) {
            UpdateProfileImage();
            return;
        }

        StartAnimationLoop();
        RenderAnimationFrame(0.0);
    }

    private void OnCompositionRendering(object? sender, EventArgs e) {
        if (!_isAnimationRunning) {
            return;
        }

        var elapsed = _animationStopwatch.Elapsed;
        var t = Math.Clamp(elapsed.TotalMilliseconds / _animationDuration.TotalMilliseconds, 0.0, 1.0);
        var eased = 1.0 - Math.Pow(1.0 - t, 3.0);
        RenderAnimationFrame(eased);

        if (t >= 1.0) {
            StopAnimationLoop();
            _displayedProfile = _currentProfile;
            _displayedModeProgress = _isPersonalised ? 1.0 : 0.0;
            IsProfileMorphing = false;
            VisualFromProfile = _currentProfile;
            VisualToProfile = _currentProfile;
            VisualProfileBlendProgress = 1.0;
            VisualModeProgress = _displayedModeProgress;
        }
    }

    private void RenderAnimationFrame(double eased) {
        if (_animationFromProfile is null || _animationToProfile is null) {
            UpdateProfileImage();
            return;
        }

        var blend = _animateProfileBlend ? eased : 1.0;
        var modeProgress = Lerp(_animationFromMode, _animationToMode, eased);
        _displayedModeProgress = modeProgress;
        VisualFromProfile = _animationFromProfile;
        VisualToProfile = _animationToProfile;
        VisualProfileBlendProgress = blend;
        VisualModeProgress = modeProgress;
    }

    private ProfileModel CaptureCurrentVisualProfile() {
        var fromProfile = _visualFromProfile ?? _currentProfile;
        var toProfile = _visualToProfile ?? _currentProfile;
        var blend = Math.Clamp(_visualProfileBlendProgress, 0.0, 1.0);

        if (ReferenceEquals(fromProfile, toProfile) || blend <= 0.0001) {
            return fromProfile;
        }

        if (blend >= 0.9999) {
            return toProfile;
        }

        return BlendProfiles(fromProfile, toProfile, blend);
    }

    private static ProfileModel BlendProfiles(ProfileModel fromProfile, ProfileModel toProfile, double blend) {
        var leftData = BlendValues(fromProfile.LeftData, toProfile.LeftData, blend);
        var rightData = BlendValues(fromProfile.RightData, toProfile.RightData, blend);
        var colour = Lerp(fromProfile.Colour, toProfile.Colour, blend);

        return new ProfileModel(toProfile.Name, colour, leftData, rightData);
    }

    private static double[] BlendValues(IReadOnlyList<double> fromValues, IReadOnlyList<double> toValues, double blend) {
        var count = Math.Min(fromValues.Count, toValues.Count);
        var values = new double[count];

        for (var i = 0; i < count; i++) {
            values[i] = Lerp(fromValues[i], toValues[i], blend);
        }

        return values;
    }

    private void UpdateProfileImage() {
        StopAnimationLoop();
        _displayedProfile = _currentProfile;
        _displayedModeProgress = _isPersonalised ? 1.0 : 0.0;
        IsProfileMorphing = false;
        VisualFromProfile = _currentProfile;
        VisualToProfile = _currentProfile;
        VisualProfileBlendProgress = 1.0;
        VisualModeProgress = _displayedModeProgress;
    }

    private void ExportHearingProfiles() {
        var renderSize = GetClampedExportRenderSize();
        var exportDirectory = _profileExportService.ExportProfiles(Devices, renderSize, UseBitmapProfileRenderer);
        var directoryName = Path.GetFileName(exportDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        ExportStatusText = $"Exported transparent PNGs to renders/{directoryName}";
        ExportRenderSizeText = renderSize.ToString();

        var fullPath = Path.GetFullPath("renders/" + directoryName);
        Process.Start("explorer.exe", fullPath);
    }

    private int GetClampedExportRenderSize() {
        return int.TryParse(ExportRenderSizeText, out var parsed)
            ? Math.Clamp(parsed, MinExportRenderSize, MaxExportRenderSize)
            : 1024;
    }

    private void StartAnimationLoop() {
        if (_isAnimationRunning) {
            StopAnimationLoop();
        }

        _isAnimationRunning = true;
        _animationStopwatch.Restart();
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void StopAnimationLoop() {
        if (!_isAnimationRunning) {
            return;
        }

        _isAnimationRunning = false;
        _animationStopwatch.Stop();
        _animationStopwatch.Reset();
        CompositionTarget.Rendering -= OnCompositionRendering;
    }

    private IReadOnlyDictionary<string, ProfileModel> BuildProfiles() {
        return new Dictionary<string, ProfileModel> {
            ["Callum"] = new ProfileModel(
                "Callum",
                0.45,
                [13.688053, 1.567564, 9.559364, -15.043674, -1.586667, 0.576336, -6.451581, -4.674792, -1.964447, -1.165475, -2.944404, -3.545126],
                [4.688053, 7.567564, 3.559364, 4.043674, 2.586667, 3.576336, -4.451581, -6.674792, 2.964447, -6.165475, -5.944404, 1.545126]),
            ["Studio"] = new ProfileModel(
                "Studio",
                0.10,
                [1.2, 0.8, 0.4, -0.3, -0.7, -0.4, 0.2, 0.5, 0.1, -0.2, -0.5, -0.3],
                [1.0, 0.7, 0.5, -0.2, -0.6, -0.5, 0.1, 0.6, 0.2, -0.1, -0.4, -0.2]),
            ["Travel"] = new ProfileModel(
                "Travel",
                0.78,
                [2.0, 1.8, 1.4, 0.7, 0.1, -0.8, -2.2, -4.5, -6.8, -7.5, -5.6, -3.2],
                [1.7, 1.5, 1.2, 0.5, -0.2, -1.2, -2.8, -5.0, -7.2, -7.8, -5.9, -3.5]),
        };
    }

    private IReadOnlyList<DeviceModel> BuildDevices() {
        return new[]
        {
            new DeviceModel("nuratrue-pro", "NuraTrue Pro 523", 80, "NTP-523-84A2197", "3.2.1", new[] { Profiles["Callum"], Profiles["Studio"], Profiles["Travel"] }),
            new DeviceModel("nuraloop", "NuraLoop", 62, "NLP-114-72C1901", "2.8.4", new[] { Profiles["Callum"], Profiles["Travel"] }),
            new DeviceModel("nuraphone", "nuraphone", 47, "NPH-008-44B9036", "4.1.0", new[] { Profiles["Callum"] }),
        };
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

}
