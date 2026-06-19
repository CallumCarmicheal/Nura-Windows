using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

using NuraLib.Devices;
using NuraLib.Rendering;

using NuraPopupWpf.Models;
using NuraPopupWpf.Services;

namespace NuraPopupWpf.ViewModels;

public sealed class NuraDeviceViewModel : DeviceModel, IAsyncDisposable {
    private const int DefaultProfileSlotCount = 3;

    private readonly IReadOnlyList<ProfileModel> _fallbackProfiles;
    private readonly NuraProfileBitmapRenderer _renderer = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _connectToNura;
    private bool _hasAuthenticatedWithNura;
    private bool _isBusy;
    private bool _isAutoSetupInProgress;
    private bool _autoSetupFailed;
    private bool _hasLocalSession;
    private bool _isMonitoring;
    private bool _requiresProvisioning;
    private ConnectedNuraDevice? _subscribedLiveDevice;
    private string? _lastAutoSetupFailureKey;
    private string _provisionDisabledReason = string.Empty;
    private string _operationStatusText = string.Empty;
    private string _operationStageCode = string.Empty;
    private StatusTone _operationStatusTone = StatusTone.Neutral;
    private bool _isOperationStatusVisible;
    private CancellationTokenSource? _operationStatusResetCts;
    private string? _lastDebugStatusText;
    private StatusTone? _lastDebugStatusTone;

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
                OnPropertyChanged(nameof(CanUseFeatureControls));
                RaiseReadinessStatusChanged();
            }
        }
    }

    public bool CanInteract => IsConnected;

    public bool IsAutoSetupInProgress {
        get => _isAutoSetupInProgress;
        private set {
            if (SetProperty(ref _isAutoSetupInProgress, value)) {
                RaiseReadinessStatusChanged();
            }
        }
    }

    public bool HasLocalSession {
        get => _hasLocalSession;
        private set {
            if (SetProperty(ref _hasLocalSession, value)) {
                OnPropertyChanged(nameof(CanUseFeatureControls));
                RaiseReadinessStatusChanged();
            }
        }
    }

    public bool IsMonitoring {
        get => _isMonitoring;
        private set {
            if (SetProperty(ref _isMonitoring, value)) {
                RaiseReadinessStatusChanged();
            }
        }
    }

    public bool RequiresProvisioning {
        get => _requiresProvisioning;
        private set {
            if (SetProperty(ref _requiresProvisioning, value)) {
                RefreshWarningState();
                OnPropertyChanged(nameof(CanProvision));
                OnPropertyChanged(nameof(CanUseFeatureControls));
                RaiseReadinessStatusChanged();
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
        private set {
            if (SetProperty(ref _operationStatusText, value)) {
                OnPropertyChanged(nameof(DisplayStatusText));
            }
        }
    }

    public string OperationStageCode {
        get => _operationStageCode;
        private set => SetProperty(ref _operationStageCode, value);
    }

    private bool HasOperationError => _operationStatusTone == StatusTone.Error &&
                                      !string.IsNullOrWhiteSpace(OperationStatusText);

    public string DisplayStatusText => _isOperationStatusVisible || HasOperationError
        ? OperationStatusText
        : ReadinessStatusText;

    public StatusTone DisplayStatusTone => _isOperationStatusVisible || HasOperationError
        ? _operationStatusTone
        : StatusTone.Neutral;

    // The shell status strip should not cover the visualiser with routine readiness text.
    public bool IsDisplayStatusVisible => _isOperationStatusVisible || HasOperationError;

    public bool CanChangeImmersionControl =>
        !IsLive || (LiveDevice is not null &&
                    LiveDevice.Info.Supports(NuraAudioCapabilities.Immersion));

    public bool CanUseLocalCommands =>
        !IsLive || (LiveDevice is not null &&
                    LiveDevice.IsConnected &&
                    LiveDevice.HasPersistentDeviceKey);

    public bool CanUseFeatureControls =>
        !IsLive || (CanUseLocalCommands && HasLocalSession && !RequiresProvisioning);

    public string ReadinessStatusText {
        get {
            if (!IsLive) {
                return "Demo device";
            }

            if (!IsConnected) {
                return "Disconnected";
            }

            if (IsAutoSetupInProgress) {
                return "Automatic setup running";
            }

            if (IsBusy) {
                return "Working";
            }

            if (_autoSetupFailed) {
                return "Automatic setup failed";
            }

            if (RequiresProvisioning) {
                return !_connectToNura || !_hasAuthenticatedWithNura
                    ? "Needs Nura login for provisioning"
                    : "Needs provisioning";
            }

            if (!HasLocalSession) {
                return "Discovered, local session not open";
            }

            return IsMonitoring ? "Ready and monitoring" : "Local session ready";
        }
    }

    public bool SupportsGesture(NuraButtonGesture gesture) {
        return LiveDevice?.Info.SupportsButtonGesture(gesture) ?? gesture is not NuraButtonGesture.TripleTap;
    }

    public int? CurrentProfileId => LiveDevice?.Profiles.ProfileId;

    public string DeviceFamilyText =>
        LiveDevice is null
            ? "Demo"
            : NuraDeviceCapabilities.GetDebugName(LiveDevice.Info.DeviceType);

    public string DeviceCapabilitySummary {
        get {
            if (LiveDevice is null) {
                return "Demo controls";
            }

            var chips = new List<string>();
            if (LiveDevice.Info.IsTws) chips.Add("TWS");
            if (LiveDevice.Info.Supports(NuraAudioCapabilities.Anc)) chips.Add("ANC");
            if (LiveDevice.Info.Supports(NuraAudioCapabilities.AncLevel)) chips.Add("ANC level");
            if (LiveDevice.Info.Supports(NuraAudioCapabilities.Immersion)) chips.Add("Immersion");
            if (LiveDevice.Info.Supports(NuraAudioCapabilities.Spatial)) chips.Add("Spatial");
            if (LiveDevice.Info.Supports(NuraAudioCapabilities.ProEq)) chips.Add("ProEQ");
            if (LiveDevice.Info.Supports(NuraInteractionCapabilities.TouchButtons)) chips.Add("Buttons");
            if (LiveDevice.Info.Supports(NuraInteractionCapabilities.Dial)) chips.Add("Dial");
            if (LiveDevice.Info.Supports(NuraSystemCapabilities.Multipoint)) chips.Add("Multipoint");

            return chips.Count == 0 ? "Generic device" : string.Join(" / ", chips);
        }
    }

    public void SetAuthContext(bool connectToNura, bool hasAuthenticatedWithNura) {
        if (_connectToNura != connectToNura || _hasAuthenticatedWithNura != hasAuthenticatedWithNura) {
            ResetAutoSetupFailure();
        }

        _connectToNura = connectToNura;
        _hasAuthenticatedWithNura = hasAuthenticatedWithNura;

        RefreshWarningState();
        OnPropertyChanged(nameof(CanProvision));
        OnPropertyChanged(nameof(CanUseFeatureControls));
        RaiseReadinessStatusChanged();
    }

    public void ResetAutoSetupFailure() {
        _lastAutoSetupFailureKey = null;
        _autoSetupFailed = false;
        RaiseReadinessStatusChanged();
    }

    public override void AttachLiveDevice(ConnectedNuraDevice? liveDevice) {
        if (ReferenceEquals(LiveDevice, liveDevice)) {
            EnsureLiveDeviceSubscription(liveDevice);
            return;
        }

        if (_subscribedLiveDevice is not null) {
            UnsubscribeLiveDevice(_subscribedLiveDevice);
            _subscribedLiveDevice = null;
        }

        CancelOperationStatusReset();
        _isOperationStatusVisible = false;

        base.AttachLiveDevice(liveDevice);

        if (liveDevice is not null) {
            EnsureLiveDeviceSubscription(liveDevice);
            if (liveDevice.OperationStatus is { } operationStatus) {
                ApplyOperationStatus(operationStatus);
                PresentOperationStatus(operationStatus);
            }
        }

        OnPropertyChanged(nameof(CanChangeImmersionControl));
        OnPropertyChanged(nameof(CurrentProfileId));
    }

    public async Task<bool> EnsureReadyAsync(
        bool connectToNura,
        bool hasAuthenticatedWithNura,
        bool forceProvision,
        bool refreshAfterConnect,
        CancellationToken cancellationToken = default
    ) {
        if (LiveDevice is null) {
            return false;
        }

        SetAuthContext(connectToNura, hasAuthenticatedWithNura);

        return await ExecuteLiveOperationAsync(
            async ct => {
                if (!LiveDevice.IsConnected) {
                    throw new InvalidOperationException("The device is not connected over Bluetooth.");
                }

                if (forceProvision || await LiveDevice.RequiresProvisioningAsync(ct)) {
                    if (!_connectToNura || !_hasAuthenticatedWithNura) {
                        throw new InvalidOperationException("Provisioning requires an authenticated Nura session.");
                    }

                    var provisioningResult = await LiveDevice.EnsureProvisionedAsync(forceProvision: true, cancellationToken: ct);
                    if (!provisioningResult.Success) {
                        throw new InvalidOperationException(provisioningResult.Error switch {
                            NuraProvisioningError.NotAuthenticated => "Provisioning requires an authenticated Nura session.",
                            _ => "Provisioning failed."
                        });
                    }
                }

                await LiveDevice.ConnectLocalAsync(ct);

                if (refreshAfterConnect) {
                    await LiveDevice.RefreshAsync(ct);
                }

                await LiveDevice.StartMonitoringAsync(ct);
                ApplyFromLiveDevice();
            },
            cancellationToken);
    }

    public async Task<bool> TryAutoSetupAsync(
        bool connectToNura,
        bool hasAuthenticatedWithNura,
        CancellationToken cancellationToken = default
    ) {
        if (LiveDevice is null || !IsConnected) {
            return false;
        }

        if (IsAutoSetupInProgress || IsBusy) {
            return false;
        }

        if (HasLocalSession && IsMonitoring && !RequiresProvisioning) {
            return true;
        }

        SetAuthContext(connectToNura, hasAuthenticatedWithNura);

        var failureKey = BuildAutoSetupFailureKey(connectToNura, hasAuthenticatedWithNura);
        if (string.Equals(_lastAutoSetupFailureKey, failureKey, StringComparison.Ordinal)) {
            return false;
        }

        if (LiveDevice.ProvisioningRequired && (!connectToNura || !hasAuthenticatedWithNura)) {
            _lastAutoSetupFailureKey = failureKey;
            _autoSetupFailed = false;
            SetOperationFailure("Automatic setup needs a Nura login before this device can be provisioned.");
            RaiseReadinessStatusChanged();
            return false;
        }

        IsAutoSetupInProgress = true;
        try {
            OperationStatusText = "Automatic setup starting.";
            OperationStageCode = "auto_setup";
            PresentTransientOperationStatus(StatusTone.Information);
            var success = await EnsureReadyAsync(
                connectToNura,
                hasAuthenticatedWithNura,
                forceProvision: false,
                refreshAfterConnect: true,
                cancellationToken);
            _lastAutoSetupFailureKey = success ? null : failureKey;
            _autoSetupFailed = !success;
            RaiseReadinessStatusChanged();
            return success;
        } finally {
            IsAutoSetupInProgress = false;
        }
    }

    public Task RefreshFromDeviceAsync(CancellationToken cancellationToken = default) {
        return ExecuteLiveOperationAsync(
            async ct => {
                if (LiveDevice is null) {
                    return;
                }

                await LiveDevice.RefreshAsync(ct);

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

    public async Task ApplyPersonalisationAsync(bool isPersonalised, CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            IsPersonalised = isPersonalised;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing personalisation.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.SetPersonalisationModeAsync(
                isPersonalised ? NuraPersonalisationMode.Personalised : NuraPersonalisationMode.Neutral,
                ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplyImmersionIndexAsync(int immersionIndex, CancellationToken cancellationToken = default) {
        var nextIndex = Math.Clamp(immersionIndex, 0, 6);
        if (LiveDevice is null) {
            ImmersionIndex = nextIndex;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing immersion.");
            return;
        }

        if (!CanChangeImmersionControl) {
            SetOperationFailure("Immersion control is not supported for this device.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.SetImmersionLevelAsync(NuraImmersionLevelExtensions.FromRawIndex(nextIndex), ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplyAncEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            AncEnabled = enabled;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing ANC.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.SetAncEnabledAsync(enabled, ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplyPassthroughEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            SocialMode = enabled;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing passthrough.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.SetPassthroughEnabledAsync(enabled, ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplySpatialEnabledAsync(bool enabled, CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            SpatialEnabled = enabled;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing spatial audio.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.SetSpatialEnabledAsync(enabled, ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplyAncLevelAsync(int level, CancellationToken cancellationToken = default) {
        var nextLevel = Math.Clamp(level, 0, 4);
        if (LiveDevice is null) {
            AncLevel = nextLevel;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing ANC level.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.SetAncLevelAsync(nextLevel, ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplyTouchButtonsAsync(NuraButtonConfiguration configuration, CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            TouchButtons = configuration;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing touch buttons.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.Configuration.SetTouchButtonsAsync(configuration, ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task ApplyDialAsync(NuraDialConfiguration configuration, CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            Dial = configuration;
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before changing dial bindings.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.Configuration.SetDialAsync(configuration, ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public async Task RefreshBatteryAsync(CancellationToken cancellationToken = default) {
        if (LiveDevice is null) {
            return;
        }

        if (!CanUseLocalCommands) {
            SetOperationFailure("Connect and provision this device before refreshing battery.");
            return;
        }

        await ExecuteLiveOperationAsync(
            ct => LiveDevice.State.RetrieveBatteryAsync(ct),
            cancellationToken,
            rollbackToLiveStateOnFailure: true);
    }

    public ValueTask DisposeAsync() {
        CancelOperationStatusReset();
        if (_subscribedLiveDevice is not null) {
            UnsubscribeLiveDevice(_subscribedLiveDevice);
            _subscribedLiveDevice = null;
        }

        _operationGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> ExecuteLiveOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken,
        bool rollbackToLiveStateOnFailure = false,
        bool rethrowOnFailure = false
    ) {
        if (LiveDevice is null) {
            return false;
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
            _autoSetupFailed = false;
            _lastAutoSetupFailureKey = null;
            RaiseReadinessStatusChanged();
            return true;
        } catch (Exception ex) {
            if (rollbackToLiveStateOnFailure) {
                ApplyFromLiveDevice();
            }

            SetOperationFailure(ex.Message);

            if (rethrowOnFailure) {
                throw;
            }

            return false;
        } finally {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    private void SubscribeLiveDevice(ConnectedNuraDevice liveDevice) {
        liveDevice.Changed += OnLiveDeviceEvent;
        liveDevice.OperationStatusChanged += OnLiveDeviceOperationStatusChanged;
    }

    private void EnsureLiveDeviceSubscription(ConnectedNuraDevice? liveDevice) {
        if (liveDevice is null || ReferenceEquals(_subscribedLiveDevice, liveDevice)) {
            return;
        }

        if (_subscribedLiveDevice is not null) {
            UnsubscribeLiveDevice(_subscribedLiveDevice);
        }

        SubscribeLiveDevice(liveDevice);
        _subscribedLiveDevice = liveDevice;
    }

    private void UnsubscribeLiveDevice(ConnectedNuraDevice liveDevice) {
        liveDevice.Changed -= OnLiveDeviceEvent;
        liveDevice.OperationStatusChanged -= OnLiveDeviceOperationStatusChanged;
    }

    private async void OnLiveDeviceEvent(object? sender, EventArgs e) {
        await RunOnUiThreadAsync(() => ApplyFromLiveDevice());
    }

    private async void OnLiveDeviceOperationStatusChanged(object? sender, NuraValueChangedEventArgs<NuraDeviceOperationStatus?> e) {
        await RunOnUiThreadAsync(() => {
            if (!ReferenceEquals(sender, LiveDevice)) {
                return;
            }

            ApplyOperationStatus(e.Current);
            if (e.Current is not null) {
                PresentOperationStatus(e.Current);
            }
        });
    }

    private void ApplyFromLiveDevice() {
        if (LiveDevice is null) {
            return;
        }

        Name = LiveDevice.Info.DisplayName;
        IsConnected = LiveDevice.IsConnected;
        SerialNumber = LiveDevice.Info.Serial;
        SoftwareVersion = LiveDevice.Info.FirmwareVersion.ToString(CultureInfo.InvariantCulture);
        BatteryLevel = LiveDevice.State.Battery?.BatteryPercentage;
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

        OnPropertyChanged(nameof(CurrentProfileId));
        OnPropertyChanged(nameof(CanChangeImmersionControl));
        OnPropertyChanged(nameof(CanUseFeatureControls));
        OnPropertyChanged(nameof(DeviceFamilyText));
        OnPropertyChanged(nameof(DeviceCapabilitySummary));
        RaiseReadinessStatusChanged();
    }

    private void ApplyOperationStatus(NuraDeviceOperationStatus? status) {
        if (status is null) {
            OperationStageCode = string.Empty;
            OperationStatusText = IsLive ? ConnectionStatusText : "Demo device ready.";
            _operationStatusTone = StatusTone.Neutral;
            return;
        }

        OperationStageCode = status.StageCode;
        OperationStatusText = status.Message;
        _operationStatusTone = GetStatusTone(status);
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
        _operationStatusTone = StatusTone.Error;
        RefreshWarningState();
        WarningText = message;
        PresentTransientOperationStatus(StatusTone.Error);
    }

    private void PresentOperationStatus(NuraDeviceOperationStatus status) {
        PresentTransientOperationStatus(GetStatusTone(status));
    }

    private void PresentTransientOperationStatus(StatusTone tone) {
        _operationStatusTone = tone;
        _isOperationStatusVisible = true;
        PublishDisplayStatusChanged();

        CancelOperationStatusReset();
        var cancellation = new CancellationTokenSource();
        _operationStatusResetCts = cancellation;
        _ = ResetOperationStatusAfterDelayAsync(cancellation);
    }

    private async Task ResetOperationStatusAfterDelayAsync(CancellationTokenSource cancellation) {
        try {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellation.Token);
            await RunOnUiThreadAsync(() => {
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_operationStatusResetCts, cancellation)) {
                    return;
                }

                _operationStatusResetCts = null;
                _isOperationStatusVisible = false;
                PublishDisplayStatusChanged();
            });
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        }
    }

    private void CancelOperationStatusReset() {
        var cancellation = _operationStatusResetCts;
        _operationStatusResetCts = null;
        if (cancellation is null) {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void RaiseReadinessStatusChanged() {
        OnPropertyChanged(nameof(ReadinessStatusText));
        if (!_isOperationStatusVisible) {
            PublishDisplayStatusChanged();
        }
    }

    private void PublishDisplayStatusChanged() {
        OnPropertyChanged(nameof(DisplayStatusText));
        OnPropertyChanged(nameof(DisplayStatusTone));
        OnPropertyChanged(nameof(IsDisplayStatusVisible));

        var text = DisplayStatusText;
        var tone = DisplayStatusTone;
        if (string.Equals(_lastDebugStatusText, text, StringComparison.Ordinal) && _lastDebugStatusTone == tone) {
            return;
        }

        _lastDebugStatusText = text;
        _lastDebugStatusTone = tone;
        Debug.WriteLine(
            $"[NuraPopupWpf.Status] device id={Id} name={Name} operation={LiveDevice?.OperationStatus?.Kind.ToString() ?? "none"} stage={OperationStageCode.ReplaceLineEndings(" ")} tone={tone} text={text.ReplaceLineEndings(" ")}");
    }

    private static StatusTone GetStatusTone(NuraDeviceOperationStatus status) {
        if (status.IsError) {
            return StatusTone.Error;
        }

        return status.IsCompleted ? StatusTone.Success : StatusTone.Information;
    }

    private string BuildAutoSetupFailureKey(bool connectToNura, bool hasAuthenticatedWithNura) {
        if (LiveDevice is null) {
            return "none";
        }

        return string.Join(
            "|",
            IsConnected,
            LiveDevice.HasPersistentDeviceKey,
            LiveDevice.ProvisioningRequired,
            LiveDevice.ProvisioningRequirementReason,
            connectToNura,
            hasAuthenticatedWithNura);
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
                ? new ProfileModel(name, visualisation)
                : GetFallbackProfile(name, profileId);
            profile.Thumbnail = _renderer.RenderThumbnail(profile.VisualisationData, 20).ToBitmapSource();
            profiles.Add(profile);
        }

        return profiles;
    }

    private ProfileModel GetFallbackProfile(string name, int profileId) {
        if (_fallbackProfiles.Count == 0) {
            // TODO: Check if I should be leaving this as valid in this scenario.
            return new ProfileModel(name, NuraProfileVisualisationData.Empty with { Valid = true, Colour = 0.45 });
        }

        var template = _fallbackProfiles[profileId % _fallbackProfiles.Count];
        return new ProfileModel(name, template.VisualisationData) {
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
