using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;

using NuraLib.Devices;

using NuraPopupWpf.Models;
using NuraPopupWpf.Services;

namespace NuraPopupWpf.ViewModels;

public sealed class NuraDeviceViewModel : DeviceModel, IAsyncDisposable {
    private const int DefaultProfileSlotCount = 3;

    private readonly IReadOnlyList<ProfileModel> _fallbackProfiles;
    private readonly NuraProfileRenderer _renderer = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _isApplyingSdkUpdate;
    private bool _connectToNura;
    private bool _hasAuthenticatedWithNura;
    private bool _isBusy;
    private bool _hasLocalSession;
    private bool _isMonitoring;
    private bool _requiresProvisioning;
    private string _provisionDisabledReason = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _operationStageCode = string.Empty;

    private NuraDeviceViewModel(
        string id,
        string name,
        int? batteryLevel,
        string serialNumber,
        string softwareVersion,
        IReadOnlyList<ProfileModel> profiles,
        IReadOnlyList<ProfileModel> fallbackProfiles,
        bool isConnected = true,
        bool socialMode = false,
        bool ancEnabled = true,
        bool euVolumeLimiter = false,
        int immersionIndex = 3,
        bool isPersonalised = true,
        string warningText = "",
        ConnectedNuraDevice? liveDevice = null)
        : base(
            id,
            name,
            batteryLevel,
            serialNumber,
            softwareVersion,
            profiles,
            isConnected,
            socialMode,
            ancEnabled,
            euVolumeLimiter,
            immersionIndex,
            isPersonalised,
            warningText,
            liveDevice) {
        _fallbackProfiles = fallbackProfiles;
        PropertyChanged += OnViewModelPropertyChanged;
        OperationStatusText = IsLive ? ConnectionStatusText : "Demo device ready.";
    }

    public static NuraDeviceViewModel CreateDemo(
        string id,
        string name,
        int? batteryLevel,
        string serialNumber,
        string softwareVersion,
        IReadOnlyList<ProfileModel> profiles,
        IReadOnlyList<ProfileModel> fallbackProfiles,
        bool isConnected = true,
        bool socialMode = false,
        bool ancEnabled = true,
        bool euVolumeLimiter = false,
        int immersionIndex = 3,
        bool isPersonalised = true,
        string warningText = "") {
        return new NuraDeviceViewModel(
            id,
            name,
            batteryLevel,
            serialNumber,
            softwareVersion,
            profiles,
            fallbackProfiles,
            isConnected,
            socialMode,
            ancEnabled,
            euVolumeLimiter,
            immersionIndex,
            isPersonalised,
            warningText);
    }

    public static NuraDeviceViewModel CreateLive(
        ConnectedNuraDevice liveDevice,
        IReadOnlyDictionary<string, ProfileModel> fallbackProfiles,
        bool connectToNura,
        bool hasAuthenticatedWithNura) {
        var templates = fallbackProfiles.Values.ToList();
        var viewModel = new NuraDeviceViewModel(
            GetIdentityKey(liveDevice),
            liveDevice.Info.DisplayName,
            batteryLevel: null,
            serialNumber: liveDevice.Info.Serial,
            softwareVersion: liveDevice.Info.FirmwareVersion.ToString(CultureInfo.InvariantCulture),
            profiles: [],
            fallbackProfiles: templates,
            isConnected: liveDevice.IsConnected,
            immersionIndex: liveDevice.Info.DefaultImmersionLevel.ToRawIndex(),
            isPersonalised: true,
            liveDevice: liveDevice);
        viewModel.SetAuthContext(connectToNura, hasAuthenticatedWithNura);
        viewModel.AttachLiveDevice(liveDevice);
        viewModel.ApplyFromLiveDevice();
        return viewModel;
    }

    public bool IsBusy {
        get => _isBusy;
        private set {
            if (SetProperty(ref _isBusy, value)) {
                OnPropertyChanged(nameof(CanProvision));
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public bool CanInteract => IsConnected && !IsBusy;

    public bool HasLocalSession {
        get => _hasLocalSession;
        private set => SetProperty(ref _hasLocalSession, value);
    }

    public bool IsMonitoring {
        get => _isMonitoring;
        private set => SetProperty(ref _isMonitoring, value);
    }

    public bool RequiresProvisioning {
        get => _requiresProvisioning;
        private set {
            if (SetProperty(ref _requiresProvisioning, value)) {
                RefreshWarningState();
                OnPropertyChanged(nameof(CanProvision));
            }
        }
    }

    public bool CanProvision => IsLive && IsConnected && !IsBusy && string.IsNullOrWhiteSpace(ProvisionDisabledReason);

    public string ProvisionDisabledReason {
        get => _provisionDisabledReason;
        private set => SetProperty(ref _provisionDisabledReason, value);
    }

    public string OperationStatusText {
        get => _operationStatusText;
        private set => SetProperty(ref _operationStatusText, value);
    }

    public string OperationStageCode {
        get => _operationStageCode;
        private set => SetProperty(ref _operationStageCode, value);
    }

    public bool CanChangeImmersionControl =>
        !IsLive || (LiveDevice is not null &&
                    LiveDevice.Info.Supports(NuraAudioCapabilities.Immersion) &&
                    LiveDevice.Info.DeviceType != NuraDeviceType.Nuraphone);

    public bool CanUseLocalCommands =>
        !IsLive || (LiveDevice is not null &&
                    LiveDevice.IsConnected &&
                    LiveDevice.HasPersistentDeviceKey);

    public bool SupportsGesture(NuraButtonGesture gesture) {
        return LiveDevice?.Info.SupportsButtonGesture(gesture) ?? gesture is not NuraButtonGesture.TripleTap;
    }

    public int? CurrentProfileId => LiveDevice?.Profiles.ProfileId;

    public void SetAuthContext(bool connectToNura, bool hasAuthenticatedWithNura) {
        _connectToNura = connectToNura;
        _hasAuthenticatedWithNura = hasAuthenticatedWithNura;

        RefreshWarningState();
        OnPropertyChanged(nameof(CanProvision));
    }

    public override void AttachLiveDevice(ConnectedNuraDevice? liveDevice) {
        if (ReferenceEquals(LiveDevice, liveDevice)) {
            return;
        }

        if (LiveDevice is not null) {
            UnsubscribeLiveDevice(LiveDevice);
        }

        base.AttachLiveDevice(liveDevice);

        if (liveDevice is not null) {
            SubscribeLiveDevice(liveDevice);
        }

        OnPropertyChanged(nameof(CanChangeImmersionControl));
        OnPropertyChanged(nameof(CurrentProfileId));
    }

    public async Task EnsureReadyAsync(
        bool connectToNura,
        bool hasAuthenticatedWithNura,
        bool forceProvision,
        bool refreshAfterConnect,
        CancellationToken cancellationToken = default
    ) {
        if (LiveDevice is null) {
            return;
        }

        SetAuthContext(connectToNura, hasAuthenticatedWithNura);

        await ExecuteLiveOperationAsync(
            async ct => {
                if (!LiveDevice.IsConnected) {
                    SetOperationFailure("The device is not connected over Bluetooth.");
                    return;
                }

                if (forceProvision || await LiveDevice.RequiresProvisioningAsync(ct)) {
                    if (!_connectToNura || !_hasAuthenticatedWithNura) {
                        SetOperationFailure("Provisioning requires an authenticated Nura session.");
                        return;
                    }

                    var provisioningResult = await LiveDevice.EnsureProvisionedAsync(forceProvision: true, cancellationToken: ct);
                    if (!provisioningResult.Success) {
                        SetOperationFailure(provisioningResult.Error switch {
                            NuraProvisioningError.NotAuthenticated => "Provisioning requires an authenticated Nura session.",
                            _ => "Provisioning failed."
                        });
                        return;
                    }
                }

                await LiveDevice.ConnectLocalAsync(ct);

                if (refreshAfterConnect) {
                    await LiveDevice.RefreshAsync(ct);

                    if (LiveDevice.Info.Supports(NuraAudioCapabilities.VisualisationData)) {
                        await LiveDevice.Profiles.RefreshVisualisationsAsync(
                            DefaultProfileSlotCount,
                            includeOnlineMetadata: _connectToNura && _hasAuthenticatedWithNura,
                            cancellationToken: ct);
                    }
                }

                await LiveDevice.StartMonitoringAsync(ct);
                ApplyFromLiveDevice();
            },
            cancellationToken);
    }

    public Task RefreshFromDeviceAsync(CancellationToken cancellationToken = default) {
        return ExecuteLiveOperationAsync(
            async ct => {
                if (LiveDevice is null) {
                    return;
                }

                await LiveDevice.RefreshAsync(ct);
                if (LiveDevice.Info.Supports(NuraAudioCapabilities.VisualisationData)) {
                    await LiveDevice.Profiles.RefreshVisualisationsAsync(
                        DefaultProfileSlotCount,
                        includeOnlineMetadata: _connectToNura && _hasAuthenticatedWithNura,
                        cancellationToken: ct);
                }

                ApplyFromLiveDevice();
            },
            cancellationToken);
    }

    public Task ConnectAndPrepareAsync(
        bool connectToNura,
        bool hasAuthenticatedWithNura,
        CancellationToken cancellationToken = default
    ) {
        return EnsureReadyAsync(connectToNura, hasAuthenticatedWithNura, forceProvision: false, refreshAfterConnect: true, cancellationToken: cancellationToken);
    }

    public Task ProvisionAndPrepareAsync(
        bool connectToNura,
        bool hasAuthenticatedWithNura,
        CancellationToken cancellationToken = default
    ) {
        return EnsureReadyAsync(connectToNura, hasAuthenticatedWithNura, forceProvision: true, refreshAfterConnect: true, cancellationToken: cancellationToken);
    }

    public Task SelectProfileByIndexAsync(int profileId, CancellationToken cancellationToken = default) {
        return ExecuteLiveOperationAsync(
            async ct => {
                if (LiveDevice is null) {
                    return;
                }

                await LiveDevice.Profiles.SetProfileIdAsync(profileId, ct);
            },
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public ValueTask DisposeAsync() {
        if (LiveDevice is not null) {
            UnsubscribeLiveDevice(LiveDevice);
        }

        PropertyChanged -= OnViewModelPropertyChanged;
        _operationGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ExecuteLiveOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool rollbackToLiveStateOnFailure = false,
        bool rethrowOnFailure = false
    ) {
        if (LiveDevice is null) {
            return;
        }

        await _operationGate.WaitAsync(cancellationToken);
        IsBusy = true;

        try {
            await operation(cancellationToken);
            RefreshWarningState();
            if (LiveDevice.OperationStatus is null) {
                OperationStatusText = "Ready.";
                OperationStageCode = string.Empty;
            }
        } catch (Exception ex) {
            OperationStatusText = ex.Message;
            OperationStageCode = "failed";
            WarningText = ex.Message;

            if (rollbackToLiveStateOnFailure) {
                ApplyFromLiveDevice();
            }

            if (rethrowOnFailure) {
                throw;
            }
        } finally {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_isApplyingSdkUpdate || LiveDevice is null || !CanUseLocalCommands) {
            return;
        }

        try {
            switch (e.PropertyName) {
            case nameof(IsPersonalised):
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.State.SetPersonalisationModeAsync(
                        IsPersonalised ? NuraPersonalisationMode.Personalised : NuraPersonalisationMode.Neutral,
                        ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(ImmersionIndex) when CanChangeImmersionControl:
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.State.SetImmersionLevelAsync(NuraImmersionLevelExtensions.FromRawIndex(ImmersionIndex), ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(AncEnabled):
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.State.SetAncEnabledAsync(AncEnabled, ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(SocialMode):
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.State.SetPassthroughEnabledAsync(SocialMode, ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(SpatialEnabled):
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.State.SetSpatialEnabledAsync(SpatialEnabled, ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(AncLevel) when AncLevel is int ancLevel:
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.State.SetAncLevelAsync(ancLevel, ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(TouchButtons) when TouchButtons is not null:
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.Configuration.SetTouchButtonsAsync(TouchButtons, ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            case nameof(Dial) when Dial is not null:
                await ExecuteLiveOperationAsync(
                    ct => LiveDevice.Configuration.SetDialAsync(Dial, ct),
                    CancellationToken.None,
                    rollbackToLiveStateOnFailure: true);
                break;
            }
        } catch {
        }
    }

    private void SubscribeLiveDevice(ConnectedNuraDevice liveDevice) {
        liveDevice.IsConnectedChanged += OnLiveDeviceEvent;
        liveDevice.InfoChanged += OnLiveDeviceEvent;
        liveDevice.HasPersistentDeviceKeyChanged += OnLiveDeviceEvent;
        liveDevice.LocalSessionChanged += OnLiveDeviceEvent;
        liveDevice.MonitoringChanged += OnLiveDeviceEvent;
        liveDevice.ProvisioningRequiredChanged += OnLiveDeviceEvent;
        liveDevice.OperationStatusChanged += OnLiveDeviceEvent;
        liveDevice.State.AncEnabledChanged += OnLiveDeviceEvent;
        liveDevice.State.PassthroughEnabledChanged += OnLiveDeviceEvent;
        liveDevice.State.AncLevelChanged += OnLiveDeviceEvent;
        liveDevice.State.SpatialEnabledChanged += OnLiveDeviceEvent;
        liveDevice.State.PersonalisationModeChanged += OnLiveDeviceEvent;
        liveDevice.State.ImmersionLevelChanged += OnLiveDeviceEvent;
        liveDevice.Configuration.TouchButtonsChanged += OnLiveDeviceEvent;
        liveDevice.Configuration.DialChanged += OnLiveDeviceEvent;
        liveDevice.Profiles.ProfileIdChanged += OnLiveDeviceEvent;
        liveDevice.Profiles.NamesChanged += OnLiveDeviceEvent;
        liveDevice.Profiles.VisualisationsChanged += OnLiveDeviceEvent;
    }

    private void UnsubscribeLiveDevice(ConnectedNuraDevice liveDevice) {
        liveDevice.IsConnectedChanged -= OnLiveDeviceEvent;
        liveDevice.InfoChanged -= OnLiveDeviceEvent;
        liveDevice.HasPersistentDeviceKeyChanged -= OnLiveDeviceEvent;
        liveDevice.LocalSessionChanged -= OnLiveDeviceEvent;
        liveDevice.MonitoringChanged -= OnLiveDeviceEvent;
        liveDevice.ProvisioningRequiredChanged -= OnLiveDeviceEvent;
        liveDevice.OperationStatusChanged -= OnLiveDeviceEvent;
        liveDevice.State.AncEnabledChanged -= OnLiveDeviceEvent;
        liveDevice.State.PassthroughEnabledChanged -= OnLiveDeviceEvent;
        liveDevice.State.AncLevelChanged -= OnLiveDeviceEvent;
        liveDevice.State.SpatialEnabledChanged -= OnLiveDeviceEvent;
        liveDevice.State.PersonalisationModeChanged -= OnLiveDeviceEvent;
        liveDevice.State.ImmersionLevelChanged -= OnLiveDeviceEvent;
        liveDevice.Configuration.TouchButtonsChanged -= OnLiveDeviceEvent;
        liveDevice.Configuration.DialChanged -= OnLiveDeviceEvent;
        liveDevice.Profiles.ProfileIdChanged -= OnLiveDeviceEvent;
        liveDevice.Profiles.NamesChanged -= OnLiveDeviceEvent;
        liveDevice.Profiles.VisualisationsChanged -= OnLiveDeviceEvent;
    }

    private async void OnLiveDeviceEvent(object? sender, EventArgs e) {
        await RunOnUiThreadAsync(() => ApplyFromLiveDevice());
    }

    private void ApplyFromLiveDevice() {
        if (LiveDevice is null) {
            return;
        }

        _isApplyingSdkUpdate = true;

        try {
            Name = LiveDevice.Info.DisplayName;
            IsConnected = LiveDevice.IsConnected;
            SerialNumber = LiveDevice.Info.Serial;
            SoftwareVersion = LiveDevice.Info.FirmwareVersion.ToString(CultureInfo.InvariantCulture);
            BatteryLevel = null;
            SupportsAnc = LiveDevice.Info.Supports(NuraAudioCapabilities.Anc);
            SupportsAncLevel = LiveDevice.Info.Supports(NuraAudioCapabilities.AncLevel);
            SupportsSpatial = LiveDevice.Info.Supports(NuraAudioCapabilities.Spatial);
            SupportsTouchButtons = LiveDevice.Info.Supports(NuraInteractionCapabilities.TouchButtons);
            SupportsDial = LiveDevice.Info.Supports(NuraInteractionCapabilities.Dial);
            SupportsEuVolumeLimiter = false;

            if (LiveDevice.State.AncEnabled is bool ancEnabled) {
                AncEnabled = ancEnabled;
            }

            if (LiveDevice.State.PassthroughEnabled is bool passthroughEnabled) {
                SocialMode = passthroughEnabled;
            }

            if (LiveDevice.State.SpatialEnabled is bool spatialEnabled) {
                SpatialEnabled = spatialEnabled;
            }

            if (LiveDevice.State.PersonalisationMode is NuraPersonalisationMode mode) {
                IsPersonalised = mode == NuraPersonalisationMode.Personalised;
            }

            if (LiveDevice.State.ImmersionLevel is NuraImmersionLevel immersionLevel) {
                ImmersionIndex = immersionLevel.ToRawIndex();
            }

            AncLevel = LiveDevice.State.AncLevel;
            TouchButtons = LiveDevice.Configuration.TouchButtons;
            Dial = LiveDevice.Configuration.Dial;
            Profiles = BuildProfilesFromLiveDevice(LiveDevice);
            HasLocalSession = LiveDevice.HasLocalSession;
            IsMonitoring = LiveDevice.IsMonitoring;
            RequiresProvisioning = LiveDevice.ProvisioningRequired || !LiveDevice.HasPersistentDeviceKey;
            ApplyOperationStatus(LiveDevice.OperationStatus);
            RefreshWarningState();
        } finally {
            _isApplyingSdkUpdate = false;
        }

        OnPropertyChanged(nameof(CurrentProfileId));
        OnPropertyChanged(nameof(CanChangeImmersionControl));
    }

    private void ApplyOperationStatus(NuraDeviceOperationStatus? status) {
        if (status is null) {
            OperationStageCode = string.Empty;
            OperationStatusText = IsLive ? ConnectionStatusText : "Demo device ready.";
            return;
        }

        OperationStageCode = status.StageCode;
        OperationStatusText = status.Message;
    }

    private void RefreshWarningState() {
        if (!IsLive || LiveDevice is null || !IsConnected) {
            ProvisionDisabledReason = IsLive && !IsConnected ? "Device is not connected." : string.Empty;
            WarningText = string.Empty;
            return;
        }

        if (LiveDevice.OperationStatus is { IsError: true } operationStatus && !string.IsNullOrWhiteSpace(operationStatus.Message)) {
            WarningText = operationStatus.Message;
        } else if (RequiresProvisioning) {
            WarningText = !_connectToNura || !_hasAuthenticatedWithNura
                ? "Sign in to provision this device"
                : "Provisioning required";
        } else if (LiveDevice.IsNuraNowDevice && (!_connectToNura || !_hasAuthenticatedWithNura)) {
            WarningText = "NuraNow renewal requires a Nura login";
        } else {
            WarningText = string.Empty;
        }

        if (!IsConnected) {
            ProvisionDisabledReason = "Device is not connected.";
        } else if (!_connectToNura || !_hasAuthenticatedWithNura) {
            ProvisionDisabledReason = "Sign in to provision this device.";
        } else {
            ProvisionDisabledReason = string.Empty;
        }
    }

    private void SetOperationFailure(string message) {
        OperationStatusText = message;
        OperationStageCode = "failed";
        WarningText = message;
        RefreshWarningState();
    }

    private IReadOnlyList<ProfileModel> BuildProfilesFromLiveDevice(ConnectedNuraDevice liveDevice) {
        var profileCount = Math.Max(DefaultProfileSlotCount, (liveDevice.Profiles.ProfileId ?? -1) + 1);

        if (liveDevice.Profiles.Names.Count > 0) {
            profileCount = Math.Max(profileCount, liveDevice.Profiles.Names.Keys.Max() + 1);
        }

        if (liveDevice.Profiles.Visualisations.Count > 0) {
            profileCount = Math.Max(profileCount, liveDevice.Profiles.Visualisations.Keys.Max() + 1);
        }

        var profiles = new List<ProfileModel>(profileCount);
        for (var profileId = 0; profileId < profileCount; profileId++) {
            var name = liveDevice.Profiles.Names.TryGetValue(profileId, out var profileName)
                ? profileName
                : $"Profile {profileId + 1}";

            NuraProfileVisualisationData? visualisation = null;
            if (liveDevice.Profiles.Visualisations.TryGetValue(profileId, out var slotVisualisation)) {
                visualisation = slotVisualisation;
            } else if (liveDevice.Profiles.ProfileId == profileId && liveDevice.Profiles.CurrentVisualisation is { Valid: true } currentVisualisation) {
                visualisation = currentVisualisation;
            }

            var profile = visualisation is { Valid: true }
                ? new ProfileModel(name, visualisation.Colour, visualisation.LeftData.ToArray(), visualisation.RightData.ToArray())
                : GetFallbackProfile(name, profileId);
            profile.Thumbnail = _renderer.RenderThumbnail(profile, 20);
            profiles.Add(profile);
        }

        return profiles;
    }

    private ProfileModel GetFallbackProfile(string name, int profileId) {
        if (_fallbackProfiles.Count == 0) {
            return new ProfileModel(name, 0.45, [], []);
        }

        var template = _fallbackProfiles[profileId % _fallbackProfiles.Count];
        return new ProfileModel(name, template.Colour, template.LeftData.ToArray(), template.RightData.ToArray()) {
            Thumbnail = template.Thumbnail
        };
    }

    private async Task RunOnUiThreadAsync(Action action) {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess()) {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private static string GetIdentityKey(NuraDevice liveDevice) {
        return $"{liveDevice.Info.Serial}|{liveDevice.Info.DeviceAddress}";
    }
}
