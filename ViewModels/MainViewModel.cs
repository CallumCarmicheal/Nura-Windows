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
    private readonly WindowPreferencesService _windowPreferencesService = new();
    private readonly ObservableCollection<string> _modes = new ObservableCollection<string>(new[] { "Neutral", "Personalised" });
    private readonly ObservableCollection<WindowAnchorOption> _windowAnchorOptions;
    private readonly Stopwatch _animationStopwatch = new();
    private readonly WindowPreferences _windowPreferences;

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
    private bool _isSerialVisible;
    private bool _isCompactProfileSelectorOpen;
    private bool _useBitmapProfileRenderer;
    private bool _isProfileMorphing;
    private bool _hasCompletedAuthenticationGate;
    private bool _isAuthenticationCodeStep;
    private bool _connectToNura;
    private bool _hasAuthenticatedWithNura;
    private string _authenticationEmail = string.Empty;
    private string _authenticationCode = string.Empty;
    private string _authenticationStatusText = "Sign in with your email, or skip if your device keys are already stored locally.";
    private string _exportRenderSizeText = "1024";
    private string _exportStatusText = "Save transparent PNG renders for every profile on every available device.";
    private WindowAnchorOption _selectedWindowAnchorOption = null!;

    public MainViewModel() {
        _windowPreferences = _windowPreferencesService.Load();
        _windowAnchorOptions = new ObservableCollection<WindowAnchorOption>(BuildWindowAnchorOptions());
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
        _selectedWindowAnchorOption = _windowAnchorOptions.First(option => option.Mode == _windowPreferences.AnchorMode);

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
        SubmitAuthenticationEmailCommand = new RelayCommand(_ => SubmitAuthenticationEmail());
        VerifyAuthenticationCodeCommand = new RelayCommand(_ => VerifyAuthenticationCode());
        BackToAuthenticationEmailCommand = new RelayCommand(_ => BackToAuthenticationEmail());
        SkipAuthenticationCommand = new RelayCommand(_ => SkipAuthentication());
        LogoutAuthenticationCommand = new RelayCommand(_ => LogoutAuthentication());
        ExportHearingProfilesCommand = new RelayCommand(_ => {
            ExportHearingProfiles();
        });
    }

    public ObservableCollection<DeviceModel> Devices { get; }

    public IReadOnlyDictionary<string, ProfileModel> Profiles { get; }

    public ObservableCollection<string> Modes => _modes;

    public ObservableCollection<WindowAnchorOption> WindowAnchorOptions => _windowAnchorOptions;

    public ICommand ToggleExpandedCommand { get; }

    public ICommand ShowDevicePageCommand { get; }

    public ICommand ShowSettingsPageCommand { get; }

    public ICommand SelectModeCommand { get; }

    public ICommand ToggleSerialVisibilityCommand { get; }

    public ICommand SubmitAuthenticationEmailCommand { get; }

    public ICommand VerifyAuthenticationCodeCommand { get; }

    public ICommand BackToAuthenticationEmailCommand { get; }

    public ICommand SkipAuthenticationCommand { get; }

    public ICommand LogoutAuthenticationCommand { get; }

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

    public bool HasCompletedAuthenticationGate {
        get => _hasCompletedAuthenticationGate;
        private set {
            if (SetProperty(ref _hasCompletedAuthenticationGate, value)) {
                OnPropertyChanged(nameof(IsAuthenticationPage));
                OnPropertyChanged(nameof(IsMainAppVisible));
            }
        }
    }

    public bool IsAuthenticationPage => !HasCompletedAuthenticationGate;

    public bool IsMainAppVisible => HasCompletedAuthenticationGate;

    public bool IsAuthenticationCodeStep {
        get => _isAuthenticationCodeStep;
        private set => SetProperty(ref _isAuthenticationCodeStep, value);
    }

    public string AuthenticationEmail {
        get => _authenticationEmail;
        set {
            if (SetProperty(ref _authenticationEmail, value)) {
                OnPropertyChanged(nameof(AccountEmailDisplay));
            }
        }
    }

    public string AuthenticationCode {
        get => _authenticationCode;
        set => SetProperty(ref _authenticationCode, NormaliseAuthenticationCode(value));
    }

    public string AuthenticationStatusText {
        get => _authenticationStatusText;
        private set => SetProperty(ref _authenticationStatusText, value);
    }

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
            OnPropertyChanged(nameof(CurrentConnectionStatusText));
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

    public bool ConnectToNura {
        get => _connectToNura;
        set => SetProperty(ref _connectToNura, value);
    }

    public string ConnectToNuraSubtitle => "Devices on the nura-now subscription would need to phone home roughly every 30 days, enable this if you need to stop your device from locking.";

    public bool HasAuthenticatedWithNura {
        get => _hasAuthenticatedWithNura;
        private set {
            if (SetProperty(ref _hasAuthenticatedWithNura, value)) {
                OnPropertyChanged(nameof(AccountNameDisplay));
                OnPropertyChanged(nameof(AccountEmailDisplay));
            }
        }
    }

    public string ActiveProfileRendererLabel => UseBitmapProfileRenderer ? "Bitmap renderer" : "Shape renderer";

    public string ProfileRendererSubtitle => UseBitmapProfileRenderer
        ? "Use the nura renderer (slower)."
        : "Use the Shape renderer (faster but not as accurate).";

    public WindowAnchorOption SelectedWindowAnchorOption {
        get => _selectedWindowAnchorOption;
        set {
            if (value is null || !SetProperty(ref _selectedWindowAnchorOption, value)) {
                return;
            }

            _windowPreferences.AnchorMode = value.Mode;
            _windowPreferencesService.Save(_windowPreferences);
            OnPropertyChanged(nameof(WindowAnchorSubtitle));
            OnPropertyChanged(nameof(SelectedWindowAnchorModeValue));
        }
    }

    public WindowAnchorMode SelectedWindowAnchorModeValue => SelectedWindowAnchorOption.Mode;

    public string WindowAnchorSubtitle => SelectedWindowAnchorOption.Subtitle;

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

    public string CurrentConnectionStatusText => CurrentDevice.ConnectionStatusText;

    public string AccountNameDisplay => HasAuthenticatedWithNura ? "Connected account" : "Local device access";

    public string AccountEmailDisplay => HasAuthenticatedWithNura
        ? AuthenticationEmail
        : "Offline mode using stored keys";

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

    private void SubmitAuthenticationEmail() {
        var email = AuthenticationEmail.Trim();
        if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) {
            AuthenticationStatusText = "Enter a valid email address to continue.";
            return;
        }

        AuthenticationEmail = email;
        AuthenticationCode = string.Empty;
        IsAuthenticationCodeStep = true;
        AuthenticationStatusText = $"Enter the 6-digit code sent to {email}.";
    }

    private void VerifyAuthenticationCode() {
        AuthenticationCode = NormaliseAuthenticationCode(AuthenticationCode);
        if (AuthenticationCode.Length != 6) {
            AuthenticationStatusText = "Enter the full 6-digit verification code.";
            return;
        }

        HasAuthenticatedWithNura = true;
        ConnectToNura = true;
        IsAuthenticationCodeStep = false;
        HasCompletedAuthenticationGate = true;
        AuthenticationStatusText = $"Connected to Nura as {AuthenticationEmail}.";
    }

    private void BackToAuthenticationEmail() {
        IsAuthenticationCodeStep = false;
        AuthenticationCode = string.Empty;
        AuthenticationStatusText = "Update your email address or request a new 6-digit code.";
    }

    private void SkipAuthentication() {
        AuthenticationCode = string.Empty;
        IsAuthenticationCodeStep = false;
        HasAuthenticatedWithNura = false;
        HasCompletedAuthenticationGate = true;
        AuthenticationStatusText = "Continuing with locally stored device keys.";
    }

    private void LogoutAuthentication() {
        AuthenticationEmail = string.Empty;
        AuthenticationCode = string.Empty;
        ConnectToNura = false;
        HasAuthenticatedWithNura = false;
        IsAuthenticationCodeStep = false;
        HasCompletedAuthenticationGate = false;
        AuthenticationStatusText = "Sign in with your email, or skip if your device keys are already stored locally.";
    }

    public bool TryGetRememberedWindowPosition(out Point position) {
        if (_windowPreferences.LastLeft.HasValue && _windowPreferences.LastTop.HasValue) {
            position = new Point(_windowPreferences.LastLeft.Value, _windowPreferences.LastTop.Value);
            return true;
        }

        position = default;
        return false;
    }

    public void SaveRememberedWindowPosition(double left, double top) {
        _windowPreferences.LastLeft = left;
        _windowPreferences.LastTop = top;
        _windowPreferencesService.Save(_windowPreferences);
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
            new DeviceModel("nuraloop", "NuraLoop", 62, "NLP-114-72C1901", "2.8.4", new[] { Profiles["Callum"], Profiles["Travel"] }, isConnected: false, socialMode: true, ancEnabled: false, euVolumeLimiter: true),
            new DeviceModel("nuraphone", "nuraphone", 47, "NPH-008-44B9036", "4.1.0", new[] { Profiles["Callum"] }, isConnected: true, socialMode: false, ancEnabled: true, euVolumeLimiter: false),
        };
    }

    private static IEnumerable<WindowAnchorOption> BuildWindowAnchorOptions() {
        yield return new WindowAnchorOption(
            WindowAnchorMode.Taskbar,
            "Taskbar",
            "Open near the taskbar and expand away from it.");

        yield return new WindowAnchorOption(
            WindowAnchorMode.RememberLastPosition,
            "Remember last position",
            "Reopen where the window was last placed.");

        yield return new WindowAnchorOption(
            WindowAnchorMode.Center,
            "Center",
            "Open mid-screen and keep expansion centered.");
    }

    private static string NormaliseAuthenticationCode(string? value) {
        return new string((value ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

}
