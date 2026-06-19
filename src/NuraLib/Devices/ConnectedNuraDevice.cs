using NuraLib.Configuration;
using NuraLib.Crypto;
using NuraLib.Auth;
using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Utilities.Docs;
using NuraLib.Transport;
using System.Threading.Channels;

namespace NuraLib.Devices;

/// <summary>
/// Represents a Nura device that is currently connected and can participate in live operations.
/// </summary>
public sealed class ConnectedNuraDevice : NuraDevice {
    private const string Source = nameof(ConnectedNuraDevice);
    private const int DefaultProfileSlotCount = 3;
    private static readonly TimeSpan NuraNowProvisioningRefreshInterval = TimeSpan.FromDays(30);
    private readonly NuraConfigState _state;
    private readonly NuraAuthManager _authManager;
    private readonly NuraClientLogger _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _sessionStopGate = new(1, 1);
    private readonly Dictionary<int, NuraAncState> _profileAncStates = [];
    private readonly Dictionary<int, NuraClassicKickitParams> _classicKickitParams = [];
    private ConnectedNuraDeviceSession? _session;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
    private Channel<GaiaResponse>? _monitoringIndications;
    private ConnectedNuraDeviceSession? _monitoringSession;
    private bool _hasLocalSession;
    private bool _isMonitoring;
    private bool _provisioningRequired;
    private NuraProvisioningRequirementReason _provisioningRequirementReason;
    private NuraDeviceOperationStatus? _operationStatus;

    internal ConnectedNuraDevice(NuraConfigState state, NuraAuthManager authManager, NuraDeviceConfig config, NuraClientLogger logger) : base(config) {
        _state = state;
        _authManager = authManager;
        _logger = logger;
        State = new NuraDeviceState(this);
        Configuration = new NuraDeviceConfiguration(this);
        Profiles = new NuraProfiles(this);
        RefreshProvisioningRequirement(raiseEvents: false);
    }

    /// <summary>
    /// Gets the live device state surface backed by cached values and future Bluetooth updates.
    /// </summary>
    public NuraDeviceState State { get; }

    /// <summary>
    /// Gets the live device configuration surface backed by cached values and future Bluetooth updates.
    /// </summary>
    public NuraDeviceConfiguration Configuration { get; }

    /// <summary>
    /// Gets the live profile surface backed by cached values and future Bluetooth updates.
    /// </summary>
    public NuraProfiles Profiles { get; }

    public bool HasLocalSession => _hasLocalSession;

    public bool IsMonitoring => _isMonitoring;

    public bool ProvisioningRequired => _provisioningRequired;

    public NuraProvisioningRequirementReason ProvisioningRequirementReason => _provisioningRequirementReason;

    public NuraDeviceOperationStatus? OperationStatus => _operationStatus;

    /// <summary>
    /// Raised when the headset emits an indication frame while monitoring is active.
    /// </summary>
    public event EventHandler<NuraHeadsetIndicationEventArgs>? HeadsetIndicationReceived;

    public event EventHandler<NuraValueChangedEventArgs<bool>>? LocalSessionChanged;

    public event EventHandler<NuraValueChangedEventArgs<bool>>? MonitoringChanged;

    public event EventHandler<NuraValueChangedEventArgs<bool?>>? ProvisioningRequiredChanged;

    public event EventHandler<NuraValueChangedEventArgs<NuraProvisioningRequirementReason>>? ProvisioningRequirementReasonChanged;

    public event EventHandler<NuraValueChangedEventArgs<NuraDeviceOperationStatus?>>? OperationStatusChanged;

    /// <summary>
    /// Determines whether the device still requires backend-assisted provisioning before local encrypted control can be used.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when no persistent device key is available, or when the device is marked as
    /// a host-managed NuraNow device and its last successful provisioning is older than 30 days;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public Task<bool> RequiresProvisioningAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshProvisioningRequirement(raiseEvents: true);
        return Task.FromResult(_provisioningRequired == true);
    }

    /// <summary>
    /// Ensures the device is provisioned and that a persistent device key has been recovered and stored.
    /// </summary>
    /// <param name="forceProvision">Indicates whether to force provisioning even if the device is already provisioned, this is for use with NuraNow devices.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A result describing whether provisioning succeeded and, if not, the known failure reason.
    /// </returns>
    public async Task<NuraProvisioningResult> EnsureProvisionedAsync(bool forceProvision = false, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateOperationStatus(
            NuraDeviceOperationKind.Provision,
            "session/start",
            "Starting provisioning.",
            isRunning: true,
            isCompleted: false,
            isError: false);

        var requiresProvisioning = await RequiresProvisioningAsync(cancellationToken);

        // Even if the device is already provisioned, we may still want to do the provisioning because NuraNow devices need to phone home
        // every month to stop themselves from being disabled and get updated licensing information.
        if (!requiresProvisioning && !forceProvision) {
            UpdateOperationStatus(
                NuraDeviceOperationKind.Provision,
                "complete",
                "Provisioning already satisfied.",
                isRunning: false,
                isCompleted: true,
                isError: false);
            return new NuraProvisioningResult(true);
        }

        if (!_authManager.HasStoredCredentials) {
            UpdateOperationStatus(
                NuraDeviceOperationKind.Provision,
                "failed",
                "Provisioning requires an authenticated Nura session.",
                isRunning: false,
                isCompleted: true,
                isError: true);
            return new NuraProvisioningResult(false, NuraProvisioningError.NotAuthenticated);
        }

        try {
            var userSessionId = await _authManager.EnsureProvisioningReadyAsync(cancellationToken);
            if (userSessionId is null) {
                return new NuraProvisioningResult(false, NuraProvisioningError.NotAuthenticated);
            }

            var serialNumber = int.Parse(Info.Serial, System.Globalization.CultureInfo.InvariantCulture);
            var result = await _authManager.StartProvisioningSessionAsync(
                serialNumber,
                Info.FirmwareVersion,
                Info.MaxPacketLengthHint,
                cancellationToken);

            var details = NuraSessionStartResponseParser.Parse(result.DecodedBody ?? [])
                ?? throw new InvalidOperationException("Provisioning session-start response did not contain continuation actions.");
            var sessionId = details.SessionId
                ?? throw new InvalidOperationException("Provisioning session-start response did not include a session id.");
            UpdateProvisioningStage(details.FinalEvent);

            await using IHeadsetTransport transport = new RfcommHeadsetTransport(_logger);
            await transport.ConnectAsync(Info.DeviceAddress, cancellationToken);

            while (true) {
                UpdateProvisioningStage(details.FinalEvent);
                var packets = await NuraProvisioningSupport.ExecuteLocalActionsAsync(details, transport, Info, cancellationToken);

                if (string.IsNullOrWhiteSpace(details.FinalEvent)) {
                    break;
                }

                var continuation = await _authManager.ContinueProvisioningAsync(
                    details.FinalEvent,
                    sessionId,
                    packets,
                    cancellationToken);
                var nextDetails = NuraSessionStartResponseParser.Parse(continuation.DecodedBody ?? []);
                if (nextDetails is null) {
                    break;
                }

                details = nextDetails;
                sessionId = details.SessionId ?? sessionId;
            }

            if (string.IsNullOrWhiteSpace(_authManager.CurrentAppEncKey)) {
                throw new InvalidOperationException("Provisioning completed without returning an app_enc key.");
            }

            var updatedDeviceConfig = DeviceConfig with {
                DeviceKey = _authManager.CurrentAppEncKey,
                LastProvisionedUtc = DateTimeOffset.UtcNow
            };
            UpdateConfig(updatedDeviceConfig);
            RefreshProvisioningRequirement(raiseEvents: true);
            var updatedDevices = _state.Configuration.Devices
                .Select(device => string.Equals(device.DeviceSerial, Info.Serial, StringComparison.OrdinalIgnoreCase)
                    ? updatedDeviceConfig
                    : device)
                .ToList();
            _state.ReplaceConfiguration(
                _state.Configuration with { Devices = updatedDevices },
                NuraStateSaveReason.DeviceKey | NuraStateSaveReason.Bootstrap,
                $"Provisioned device key for {Info.Serial}.");

            _logger.Information(Source, $"Provisioned persistent device key for {Info.Serial}.");
            UpdateOperationStatus(
                NuraDeviceOperationKind.Provision,
                "complete",
                "Provisioning complete.",
                isRunning: false,
                isCompleted: true,
                isError: false);
            return new NuraProvisioningResult(true);
        } catch (InvalidOperationException ex) when (!_authManager.HasStoredCredentials || ex.Message.Contains("stored authenticated session", StringComparison.OrdinalIgnoreCase)) {
            _logger.Warning(Source, $"Provisioning failed for {Info.Serial}: {ex.Message}");
            UpdateOperationStatus(
                NuraDeviceOperationKind.Provision,
                "failed",
                ex.Message,
                isRunning: false,
                isCompleted: true,
                isError: true);
            return new NuraProvisioningResult(false, NuraProvisioningError.NotAuthenticated);
        } catch (Exception ex) {
            _logger.Error(Source, $"Provisioning failed for {Info.Serial}: {ex.Message}", ex);
            UpdateOperationStatus(
                NuraDeviceOperationKind.Provision,
                "failed",
                ex.Message,
                isRunning: false,
                isCompleted: true,
                isError: true);
            return new NuraProvisioningResult(false, NuraProvisioningError.Unknown);
        }
    }

    /// <summary>
    /// Establishes a local encrypted control session to the connected device.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task ConnectLocalAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);
    }

    /// <summary>
    /// Starts device-specific monitoring so cached state can be kept in sync from Bluetooth indications.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task StartMonitoringAsync(CancellationToken cancellationToken = default) {
        return StartMonitoringCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Stops device-specific monitoring.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task StopMonitoringAsync(CancellationToken cancellationToken = default) {
        return StopSessionAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes the device state and profile metadata that are currently implemented by the library.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        UpdateOperationStatus(
            NuraDeviceOperationKind.Refresh,
            "refresh",
            "Refreshing device state.",
            isRunning: true,
            isCompleted: false,
            isError: false);

        try {
            await RefreshProfilesAsync(cancellationToken);
            await RefreshConfigurationAsync(cancellationToken);
            await RefreshStateAsync(cancellationToken);
            await RetrieveBatteryStatusAsync(cancellationToken);

            UpdateOperationStatus(
                NuraDeviceOperationKind.Refresh,
                "complete",
                "Device refresh complete.",
                isRunning: false,
                isCompleted: true,
                isError: false);
        } catch (Exception ex) {
            UpdateOperationStatus(
                NuraDeviceOperationKind.Refresh,
                "failed",
                ex.Message,
                isRunning: false,
                isCompleted: true,
                isError: true);
            throw;
        }
    }

    /// <summary>
    /// Refreshes the currently implemented live state values from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshStateAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);

        if (UsesClassicKickitCommands()) {
            await RefreshClassicNuraphoneDeviceSpecificDataAsync(cancellationToken);
        } else if (SupportsDirectAncStateTransport()) {
            var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
            await TryRefreshAncStateAsync(profileId, "ANC state refresh", cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.AncLevel)) {
            await RetrieveAncLevelAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.GlobalAncToggle)) {
            await RetrieveGlobalAncEnabledAsync(cancellationToken);
        }

        if (!UsesClassicKickitCommands() && Info.Supports(NuraAudioCapabilities.PersonalisedMode)) {
            await RetrievePersonalisationModeAsync(cancellationToken);
        }

        if (!UsesClassicKickitCommands() && Info.Supports(NuraAudioCapabilities.Immersion)) {
            await RetrieveImmersionLevelAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.Spatial)) {
            await RetrieveSpatialEnabledAsync(cancellationToken);
        }
    }

    internal async Task<NuraBatteryStatus?> RetrieveBatteryStatusAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        try {
            var status = await NuraLocalSessionSupport.ReadBatteryStatusAsync(_session!, _logger, cancellationToken);
            State.UpdateBattery(status);
            return status;
        } catch (GaiaCommandException ex) {
            _logger.Debug(
                Source,
                $"Battery refresh skipped for {Info.Serial}: request=0x{(ushort)ex.RequestCommandId:x4} expected=0x{(ushort)ex.ExpectedResponseCommandId:x4} actual=0x{(ushort)ex.ResponseCommandId:x4} status=0x{ex.Status:x2}");
            return null;
        } catch (Exception ex) {
            _logger.Debug(Source, $"Battery refresh skipped for {Info.Serial}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Refreshes the currently implemented profile metadata from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshProfilesAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);

        if (!Info.Supports(NuraSystemCapabilities.Profiles)) {
            return;
        }

        await RetrieveCurrentProfileIdAsync(cancellationToken);
        await RefreshProfileNamesAsync(DefaultProfileSlotCount, cancellationToken);

        if (Info.Supports(NuraAudioCapabilities.VisualisationData)) {
            await RefreshProfileVisualisationsAsync(DefaultProfileSlotCount, cancellationToken);
        }
    }

    /// <summary>
    /// Refreshes the currently implemented configuration values from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshConfigurationAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);

        if (!Info.Supports(NuraSystemCapabilities.Profiles)) {
            return;
        }

        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);

        if (Info.Supports(NuraInteractionCapabilities.TouchButtons)) {
            await RetrieveTouchButtonsAsync(cancellationToken);
        }

        if (Info.Supports(NuraInteractionCapabilities.Dial)) {
            await RetrieveDialAsync(cancellationToken);
        }
    }

    internal async Task<int?> RetrieveCurrentProfileIdAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await NuraLocalSessionSupport.ReadCurrentProfileAsync(_session!, _logger, cancellationToken);
        Profiles.UpdateProfileId(profileId);
        return profileId;
    }

    internal async Task<string?> RetrieveProfileNameAsync(int profileId, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var name = await NuraLocalSessionSupport.ReadProfileNameAsync(_session!, _logger, profileId, cancellationToken);
        Profiles.UpdateName(profileId, name);
        return name;
    }

    internal async Task<IReadOnlyDictionary<int, string>> RefreshProfileNamesAsync(int profileCount, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (profileCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(profileCount));
        }

        await EnsureConnectedAsync(cancellationToken);
        for (var profileId = 0; profileId < profileCount; profileId++) {
            var name = await NuraLocalSessionSupport.ReadProfileNameAsync(_session!, _logger, profileId, cancellationToken);
            Profiles.UpdateName(profileId, name);
        }

        return Profiles.Names;
    }

    internal async Task SetCurrentProfileIdAsync(int profileId, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);

        if (profileId < 0 || profileId > byte.MaxValue) {
            throw new ArgumentOutOfRangeException(nameof(profileId), profileId, "Profile id must fit in a single byte.");
        }

        if (RequiresTemporaryAncStateBeforeProfileSwitch()) {
            var ancState = State.Anc ?? await RetrieveAncStateAsync(cancellationToken) ?? new NuraAncState();
            await NuraLocalSessionSupport.SetTemporaryAncStateAsync(
                _session!,
                _logger,
                ancState.AncEnabled,
                ancState.PassthroughEnabled,
                cancellationToken);
        }

        await NuraLocalSessionSupport.SelectProfileAsync(_session!, _logger, profileId, cancellationToken);
        Profiles.UpdateProfileId(profileId);
        ApplyCachedProfileState(profileId);

        if (SupportsDirectAncStateTransport()) {
            try {
                await RetrieveAncStateAsync(cancellationToken);
            } catch (Exception ex) {
                _logger.Debug(Source, $"Profile ANC refresh skipped for {Info.Serial}: {ex.Message}");
            }
        }

        if (Info.Supports(NuraAudioCapabilities.PersonalisedMode)) {
            try {
                await RetrievePersonalisationModeAsync(cancellationToken);
            } catch (Exception ex) {
                _logger.Debug(Source, $"Profile personalisation refresh skipped for {Info.Serial}: {ex.Message}");
            }
        }

        if (Info.Supports(NuraAudioCapabilities.VisualisationData)) {
            try {
                await RetrieveCurrentVisualisationAsync(cancellationToken);
            } catch (Exception ex) {
                _logger.Debug(Source, $"Profile visualisation refresh skipped for {Info.Serial}: {ex.Message}");
            }
        }
    }

    internal async Task<NuraProfileVisualisationData?> RetrieveCurrentVisualisationAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var visualisation = await RetrieveVisualisationAsync(profileId, cancellationToken);
        Profiles.UpdateCurrentVisualisation(visualisation);
        return visualisation;
    }

    internal async Task<NuraProfileVisualisationData?> RetrieveVisualisationAsync(int profileId, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var visualisation = await NuraLocalSessionSupport.ReadVisualisationDataAsync(_session!, _logger, profileId, cancellationToken);
        Profiles.UpdateVisualisation(profileId, visualisation);
        return visualisation;
    }

    internal async Task<IReadOnlyDictionary<int, NuraProfileVisualisationData>> RefreshProfileVisualisationsAsync(int profileCount, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (profileCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(profileCount));
        }

        await EnsureConnectedAsync(cancellationToken);
        for (var profileId = 0; profileId < profileCount; profileId++) {
            var visualisation = await NuraLocalSessionSupport.ReadVisualisationDataAsync(_session!, _logger, profileId, cancellationToken);
            Profiles.UpdateVisualisation(profileId, visualisation);
            if (Profiles.ProfileId == profileId) {
                Profiles.UpdateCurrentVisualisation(visualisation);
            }
        }

        return Profiles.Visualisations;
    }

    internal async Task<NuraAncState?> RetrieveAncStateAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        if (!SupportsDirectAncStateTransport()) {
            if (Info.Supports(NuraAudioCapabilities.GlobalAncToggle)) {
                var enabled = await RetrieveGlobalAncEnabledAsync(cancellationToken);
                var synthesizedAncState = enabled.HasValue
                    ? new NuraAncState { AncEnabled = enabled.Value, PassthroughEnabled = false }
                    : null;
                State.UpdateAnc(synthesizedAncState);
                return synthesizedAncState;
            }

            return State.Anc;
        }

        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session!, _logger, profileId, cancellationToken);
        ApplyAncState(profileId, ancState);
        return ancState;
    }

    internal async Task SetAncStateAsync(NuraAncState state, CancellationToken cancellationToken) {
        _ = state ?? throw new ArgumentNullException(nameof(state));
        await EnsureConnectedAsync(cancellationToken);
        if (!SupportsDirectAncStateTransport()) {
            if (Info.Supports(NuraAudioCapabilities.GlobalAncToggle)) {
                await SetGlobalAncEnabledAsync(state.AncEnabled, cancellationToken);
                State.UpdateAnc(new NuraAncState {
                    AncEnabled = state.AncEnabled,
                    PassthroughEnabled = false
                });
                return;
            }

            throw new NotSupportedException($"Direct ANC state transport is not supported by device {Info.TypeName} on firmware {Info.FirmwareVersion}.");
        }

        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetAncStateAsync(_session!, _logger, profileId, state, cancellationToken);
        ApplyAncState(profileId, state);
    }

    internal async Task SetAncEnabledAsync(bool enabled, CancellationToken cancellationToken) {
        var currentState = State.Anc ?? await RetrieveAncStateAsync(cancellationToken) ?? new NuraAncState();
        await SetAncStateAsync(currentState with { AncEnabled = enabled }, cancellationToken);
    }

    internal async Task SetPassthroughEnabledAsync(bool enabled, CancellationToken cancellationToken) {
        if (!SupportsDirectAncStateTransport()) {
            throw new NotSupportedException($"Passthrough state transport is not supported by device {Info.TypeName} on firmware {Info.FirmwareVersion}.");
        }

        var currentState = State.Anc ?? await RetrieveAncStateAsync(cancellationToken) ?? new NuraAncState();
        await SetAncStateAsync(currentState with { PassthroughEnabled = enabled }, cancellationToken);
    }

    internal async Task<int?> RetrieveAncLevelAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var level = await NuraLocalSessionSupport.ReadAncLevelAsync(_session!, _logger, profileId, cancellationToken);
        State.UpdateAncLevel(level);
        return level;
    }

    internal async Task SetAncLevelAsync(int level, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetAncLevelAsync(_session!, _logger, profileId, level, cancellationToken);
        State.UpdateAncLevel(level);
    }

    internal async Task<bool?> RetrieveGlobalAncEnabledAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var enabled = await NuraLocalSessionSupport.ReadGlobalAncEnabledAsync(_session!, _logger, profileId, cancellationToken);
        State.UpdateGlobalAncEnabled(enabled);
        return enabled;
    }

    internal async Task SetGlobalAncEnabledAsync(bool enabled, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetGlobalAncEnabledAsync(_session!, _logger, profileId, enabled, cancellationToken);
        State.UpdateGlobalAncEnabled(enabled);
    }

    internal async Task<NuraPersonalisationMode?> RetrievePersonalisationModeAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);

        if (UsesClassicKickitCommands()) {
            var mode = await NuraLocalSessionSupport.ReadKickitEnabledAsync(_session!, _logger, cancellationToken);
            State.UpdatePersonalisationMode(mode);
            return mode;
        }

        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var kickitState = await NuraLocalSessionSupport.ReadKickitStateAsync(_session!, _logger, profileId, cancellationToken);
        ApplyKickitState(kickitState);
        return State.PersonalisationMode;
    }

    internal async Task SetPersonalisationModeAsync(NuraPersonalisationMode mode, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);

        if (UsesClassicKickitCommands()) {
            await NuraLocalSessionSupport.SetKickitEnabledAsync(_session!, _logger, mode, cancellationToken);
            State.UpdatePersonalisationMode(mode);
            return;
        }

        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetKickitStateAsync(
            _session!,
            _logger,
            profileId,
            levelRaw: null,
            enabled: mode == NuraPersonalisationMode.Personalised,
            cancellationToken);
        State.UpdatePersonalisationMode(mode);
    }

    internal async Task<NuraImmersionLevel?> RetrieveImmersionLevelAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);

        if (UsesClassicKickitCommands()) {
            var parameters = await NuraLocalSessionSupport.ReadKickitParamsAsync(_session!, _logger, profileId, cancellationToken);
            return ApplyClassicKickitParams(profileId, parameters);
        }

        var kickitState = await NuraLocalSessionSupport.ReadKickitStateAsync(_session!, _logger, profileId, cancellationToken);
        ApplyKickitState(kickitState);
        return State.ImmersionLevel;
    }

    internal async Task SetImmersionLevelAsync(NuraImmersionLevel level, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);

        if (UsesClassicKickitCommands()) {
            await NuraLocalSessionSupport.SetKickitParamsAsync(_session!, _logger, profileId, level, cancellationToken);
            _classicKickitParams[profileId] = NuraClassicKickitParams.FromImmersionLevel(level);
            State.UpdateImmersionLevel(level);
            State.UpdateEffectiveImmersionLevel(level);
            return;
        }

        await NuraLocalSessionSupport.SetKickitStateAsync(
            _session!,
            _logger,
            profileId,
            levelRaw: level.ToRawIndex(),
            enabled: null,
            cancellationToken);
        State.UpdateImmersionLevel(level);
        State.UpdateEffectiveImmersionLevel(level);
    }

    internal async Task<bool?> RetrieveSpatialEnabledAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var enabled = await NuraLocalSessionSupport.ReadSpatialEnabledAsync(_session!, _logger, cancellationToken);
        State.UpdateSpatialEnabled(enabled);
        return enabled;
    }

    internal async Task SetSpatialEnabledAsync(bool enabled, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        await NuraLocalSessionSupport.SetSpatialEnabledAsync(_session!, _logger, enabled, cancellationToken);
        State.UpdateSpatialEnabled(enabled);
    }

    internal async Task<NuraButtonConfiguration?> RetrieveTouchButtonsAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var configuration = await NuraLocalSessionSupport.ReadButtonConfigurationAsync(_session!, _logger, Info, profileId, cancellationToken);
        Configuration.UpdateTouchButtons(configuration);
        return configuration;
    }

    internal async Task SetTouchButtonsAsync(NuraButtonConfiguration configuration, CancellationToken cancellationToken) {
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        await EnsureConnectedAsync(cancellationToken);
        ValidateTouchButtonConfiguration(configuration);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetButtonConfigurationAsync(_session!, _logger, Info, profileId, configuration, cancellationToken);
        Configuration.UpdateTouchButtons(configuration);
    }

    internal async Task<NuraDialConfiguration?> RetrieveDialAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var configuration = await NuraLocalSessionSupport.ReadDialConfigurationAsync(_session!, _logger, profileId, cancellationToken);
        Configuration.UpdateDial(configuration);
        return configuration;
    }

    internal async Task SetDialAsync(NuraDialConfiguration configuration, CancellationToken cancellationToken) {
        _ = configuration ?? throw new ArgumentNullException(nameof(configuration));
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetDialConfigurationAsync(_session!, _logger, profileId, configuration, cancellationToken);
        Configuration.UpdateDial(configuration);
    }

    internal async Task SetVoicePromptGainAsync(NuraVoicePromptGain gain, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        await NuraLocalSessionSupport.SetVoicePromptGainAsync(_session!, _logger, gain, cancellationToken);
        Configuration.UpdateVoicePromptGain(gain);
    }

    internal async Task RefreshOnlineProfileMetadataAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Info.Supports(NuraAudioCapabilities.VisualisationData) || !_authManager.HasStoredCredentials) {
            return;
        }

        var result = await _authManager.StartDeviceSessionAsync(
            int.Parse(Info.Serial, System.Globalization.CultureInfo.InvariantCulture),
            Info.FirmwareVersion,
            Info.MaxPacketLengthHint,
            cancellationToken);
        ApplyOnlineProfileMetadata(result.DecodedBody);

        var details = result.DecodedBody is null
            ? null
            : NuraSessionStartResponseParser.Parse(result.DecodedBody);
        if (details is null || string.IsNullOrWhiteSpace(details.FinalEvent)) {
            return;
        }

        var sessionId = details.SessionId
            ?? _authManager.CurrentBluetoothSessionId
            ?? throw new InvalidOperationException("Bluetooth session id was not available during online profile metadata refresh.");

        await using IHeadsetTransport transport = new RfcommHeadsetTransport(_logger);
        await transport.ConnectAsync(Info.DeviceAddress, cancellationToken);

        while (!string.IsNullOrWhiteSpace(details.FinalEvent) &&
               details.FinalEvent.StartsWith("session/start_", StringComparison.OrdinalIgnoreCase)) {
            var packets = await NuraProvisioningSupport.ExecuteLocalActionsAsync(details, transport, Info, cancellationToken);
            var continuation = await _authManager.ContinueProvisioningAsync(details.FinalEvent, sessionId, packets, cancellationToken);
            ApplyOnlineProfileMetadata(continuation.DecodedBody);

            if (continuation.DecodedBody is null) {
                break;
            }

            var nextDetails = NuraSessionStartResponseParser.Parse(continuation.DecodedBody);
            if (nextDetails is null) {
                break;
            }

            details = nextDetails;
            sessionId = details.SessionId ?? sessionId;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken) {
        if (_session is null) {
            await EnsureConnectedCoreAsync(cancellationToken);
        }
    }

    private async Task EnsureConnectedCoreAsync(CancellationToken cancellationToken) {
        await _connectGate.WaitAsync(cancellationToken);
        try {
            if (_session is not null) {
                return;
            }

            UpdateOperationStatus(
                NuraDeviceOperationKind.ConnectLocal,
                "connect_local",
                "Opening local control session.",
                isRunning: true,
                isCompleted: false,
                isError: false);

            if (!HasPersistentDeviceKey) {
                UpdateOperationStatus(
                    NuraDeviceOperationKind.ConnectLocal,
                    "failed",
                    $"device {Info.Serial} does not have a persistent device key",
                    isRunning: false,
                    isCompleted: true,
                    isError: true);
                throw new InvalidOperationException($"device {Info.Serial} does not have a persistent device key");
            }

            try {
                var runtime = NuraSessionRuntime.Create(DeviceConfig);
                var transport = new RfcommHeadsetTransport(_logger);
                await transport.ConnectAsync(Info.DeviceAddress, cancellationToken);
                await NuraLocalSessionSupport.PerformAppHandshakeAsync(runtime, transport, _logger, cancellationToken);
                _session = new ConnectedNuraDeviceSession(runtime, transport, _logger, Info.Serial);
                _session.Disconnected += HandleSessionDisconnected;
                UpdateLocalSessionState(true);
                _logger.Information(Source, $"Local encrypted session established for {Info.Serial}.");

                if (Info.Supports(NuraSystemCapabilities.Profiles)) {
                    var profileId = await NuraLocalSessionSupport.ReadCurrentProfileAsync(_session, _logger, cancellationToken);
                    Profiles.UpdateProfileId(profileId);

                    if (SupportsDirectAncStateTransport()) {
                        await TryRefreshAncStateAsync(profileId, "Initial ANC read", cancellationToken);
                    }
                }

                UpdateOperationStatus(
                    NuraDeviceOperationKind.ConnectLocal,
                    "complete",
                    "Local control session ready.",
                    isRunning: false,
                    isCompleted: true,
                    isError: false);
            } catch (Exception ex) {
                UpdateOperationStatus(
                    NuraDeviceOperationKind.ConnectLocal,
                    "failed",
                    ex.Message,
                    isRunning: false,
                    isCompleted: true,
                    isError: true);
                await StopSessionAsync(CancellationToken.None);
                throw;
            }
        } finally {
            _connectGate.Release();
        }
    }

    private async Task<int> RequireCurrentProfileIdAsync(CancellationToken cancellationToken) {
        return Profiles.ProfileId ?? await RetrieveCurrentProfileIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("current profile id is not available");
    }

    private async Task TryRefreshAncStateAsync(int profileId, string context, CancellationToken cancellationToken) {
        try {
            var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session!, _logger, profileId, cancellationToken);
            ApplyAncState(profileId, ancState);
        } catch (GaiaCommandException ex) {
            _logger.Debug(
                Source,
                $"{context} skipped for {Info.Serial}: request=0x{(ushort)ex.RequestCommandId:x4} expected=0x{(ushort)ex.ExpectedResponseCommandId:x4} actual=0x{(ushort)ex.ResponseCommandId:x4} status=0x{ex.Status:x2}");
        } catch (Exception ex) {
            _logger.Debug(Source, $"{context} skipped for {Info.Serial}: {ex.Message}");
        }
    }

    private bool UsesClassicKickitCommands() => Info.DeviceType == NuraDeviceType.Nuraphone;

    private bool SupportsDirectAncStateTransport() =>
        Info.Supports(NuraAudioCapabilities.Anc) &&
        !Info.Supports(NuraAudioCapabilities.GlobalAncToggle) &&
        (!UsesClassicKickitCommands() || Info.FirmwareVersion > 510);

    private bool RequiresTemporaryAncStateBeforeProfileSwitch() =>
        Info.DeviceType == NuraDeviceType.Nuraphone &&
        Info.FirmwareVersion >= 535;

    private void ValidateTouchButtonConfiguration(NuraButtonConfiguration configuration) {
        ValidateButtonGestureBinding(configuration.LeftSingleTap, NuraButtonSide.Left, NuraButtonGesture.SingleTap);
        ValidateButtonGestureBinding(configuration.RightSingleTap, NuraButtonSide.Right, NuraButtonGesture.SingleTap);
        ValidateButtonGestureBinding(configuration.LeftDoubleTap, NuraButtonSide.Left, NuraButtonGesture.DoubleTap);
        ValidateButtonGestureBinding(configuration.RightDoubleTap, NuraButtonSide.Right, NuraButtonGesture.DoubleTap);
        ValidateButtonGestureBinding(configuration.LeftTripleTap, NuraButtonSide.Left, NuraButtonGesture.TripleTap);
        ValidateButtonGestureBinding(configuration.RightTripleTap, NuraButtonSide.Right, NuraButtonGesture.TripleTap);
        ValidateButtonGestureBinding(configuration.LeftTapAndHold, NuraButtonSide.Left, NuraButtonGesture.TapAndHold);
        ValidateButtonGestureBinding(configuration.RightTapAndHold, NuraButtonSide.Right, NuraButtonGesture.TapAndHold);
    }

    private void ValidateButtonGestureBinding(NuraButtonFunction? binding, NuraButtonSide side, NuraButtonGesture gesture) {
        if (binding is null) {
            return;
        }

        if (!Info.SupportsButtonGesture(gesture)) {
            throw new ArgumentException($"Gesture {gesture} is not supported on {Info.TypeName}, so {side} {gesture} cannot be assigned.", nameof(binding));
        }
    }

    private void ApplyKickitState(NuraKickitState state) {
        State.UpdatePersonalisationMode(state.Enabled
            ? NuraPersonalisationMode.Personalised
            : NuraPersonalisationMode.Neutral);
        State.UpdateImmersionLevelRaw(state.RawLevel);
        State.UpdateEffectiveImmersionLevelRaw(state.RawLevel);
    }

    private async Task RefreshClassicNuraphoneDeviceSpecificDataAsync(CancellationToken cancellationToken) {
        var currentProfileId = await RequireCurrentProfileIdAsync(cancellationToken);

        for (var profileId = 0; profileId < DefaultProfileSlotCount; profileId++) {
            try {
                var parameters = await NuraLocalSessionSupport.ReadKickitParamsAsync(_session!, _logger, profileId, cancellationToken);
                ApplyClassicKickitParams(profileId, parameters);
            } catch (Exception ex) {
                _logger.Debug(Source, $"Classic Kickit params refresh skipped for {Info.Serial}: profile={profileId} {ex.Message}");
            }

            if (SupportsDirectAncStateTransport()) {
                await TryRefreshAncStateAsync(profileId, $"Classic ANC state refresh profile={profileId}", cancellationToken);
            }
        }

        if (Info.FirmwareVersion >= 400 && Info.Supports(NuraAudioCapabilities.PersonalisedMode)) {
            try {
                await RetrievePersonalisationModeAsync(cancellationToken);
            } catch (Exception ex) {
                _logger.Debug(Source, $"Classic Kickit enabled refresh skipped for {Info.Serial}: {ex.Message}");
            }
        }

        ApplyCachedProfileState(currentProfileId);
    }

    private void ApplyAncState(int profileId, NuraAncState state) {
        _profileAncStates[profileId] = state;
        if (Profiles.ProfileId == profileId) {
            State.UpdateAnc(state);
        }
    }

    private NuraImmersionLevel? ApplyClassicKickitParams(int profileId, NuraClassicKickitParams parameters) {
        _classicKickitParams[profileId] = parameters;
        if (Profiles.ProfileId != profileId) {
            return parameters.TryToImmersionLevel(out var cachedLevel) ? cachedLevel : null;
        }

        if (parameters.TryToImmersionLevel(out var level)) {
            State.UpdateImmersionLevel(level);
            State.UpdateEffectiveImmersionLevel(level);
            return level;
        }

        _logger.Debug(
            Source,
            $"Unknown classic Kickit params for {Info.Serial}: profile={profileId} drc=0x{parameters.DrcRaw:x2} lpf=0x{parameters.LpfRaw:x2} gain=0x{parameters.GainRaw:x2}");
        State.UpdateImmersionLevel(null);
        State.UpdateEffectiveImmersionLevel(null);
        return null;
    }

    private void ApplyCachedProfileState(int profileId) {
        if (_profileAncStates.TryGetValue(profileId, out var ancState)) {
            State.UpdateAnc(ancState);
        }

        if (_classicKickitParams.TryGetValue(profileId, out var kickitParams)) {
            ApplyClassicKickitParams(profileId, kickitParams);
        }
    }

    private void ApplyOnlineProfileMetadata(Dictionary<string, object?>? responseBody) {
        if (responseBody is null) {
            return;
        }

        var slots = NuraAuthResponseParser.ExtractProfileVisualisationSlots(responseBody);
        if (slots.Count == 0) {
            return;
        }

        Profiles.ApplyOnlineProfileMetadata(slots);
        _logger.Debug(Source, $"Applied {slots.Count} online profile metadata slots for {Info.Serial}.");
    }

    internal async Task ApplyConnectionStateAsync(bool isConnected) {
        if (IsConnected == isConnected) {
            return;
        }

        RefreshProvisioningRequirement(raiseEvents: true);
        UpdateConnectionState(isConnected);

        if (!isConnected) {
            await StopSessionAsync(CancellationToken.None);
        }
    }

    internal void RefreshProvisioningRequirement() {
        RefreshProvisioningRequirement(raiseEvents: true);
    }

    private async Task StopSessionAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        await _sessionStopGate.WaitAsync(cancellationToken);
        try {
            await StopSessionCoreAsync();
        } finally {
            _sessionStopGate.Release();
        }
    }

    private async Task StopSessionCoreAsync() {
        await StopMonitoringLoopAsync();
        var session = _session;
        if (session is null) {
            return;
        }

        _session = null;
        session.Disconnected -= HandleSessionDisconnected;
        await session.DisposeAsync();
        UpdateLocalSessionState(false);
        _logger.Information(Source, $"Local session stopped for {Info.Serial}.");
    }

    private async Task StartMonitoringCoreAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);

        if (_monitoringTask is not null && !_monitoringTask.IsCompleted) {
            return;
        }

        var session = _session ?? throw new InvalidOperationException("Local control session was not available after connection.");
        var indications = Channel.CreateUnbounded<GaiaResponse>(new UnboundedChannelOptions {
            SingleWriter = true,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _monitoringSession = session;
        _monitoringIndications = indications;
        session.IndicationReceived += HandleSessionIndication;
        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = RunMonitoringLoopAsync(indications.Reader, _monitoringCts.Token);
        UpdateMonitoringState(true);
        _logger.Information(Source, $"Started monitoring loop for {Info.Serial}.");
    }

    private async Task RunMonitoringLoopAsync(ChannelReader<GaiaResponse> indications, CancellationToken cancellationToken) {
        try {
            await foreach (var frame in indications.ReadAllAsync(cancellationToken)) {
                var indication = HeadsetIndicationParser.Parse(frame);
                if (indication is null) {
                    if (_logger.IsEnabled(NuraLogLevel.Trace)) {
                        _logger.Trace(Source, $"rx.invalid_indication.command=0x{frame.CommandId:x4}");
                    }

                    continue;
                }

                HandleIndication(indication);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        } catch (Exception ex) {
            _logger.Warning(Source, $"Monitoring loop ended with error for {Info.Serial}: {ex.Message}");
        }
    }

    private void HandleIndication(HeadsetIndication indication) {
        _logger.Debug(Source, $"indication.{indication.Identifier}=0x{indication.Value:x2}");

        switch (indication.Identifier) {
            case HeadsetIndicationIdentifier.CurrentProfileChanged:
                Profiles.UpdateProfileId(indication.Value);
                ApplyCachedProfileState(indication.Value);
                break;
            case HeadsetIndicationIdentifier.AncParametersChanged:
                if (Profiles.ProfileId is { } profileId) {
                    ApplyAncState(profileId, HeadsetIndicationParser.DecodeNuraphoneAncState(indication.Value));
                } else {
                    State.UpdateAnc(HeadsetIndicationParser.DecodeNuraphoneAncState(indication.Value));
                }
                break;
            case HeadsetIndicationIdentifier.KickitEnabledChanged:
                State.UpdatePersonalisationMode(indication.Value != 0
                    ? NuraPersonalisationMode.Personalised
                    : NuraPersonalisationMode.Neutral);
                break;
            case HeadsetIndicationIdentifier.AncLevelChanged:
                State.UpdateAncLevel(indication.Value);
                break;
            case HeadsetIndicationIdentifier.KickitLevelChanged:
                if (!UsesClassicKickitCommands()) {
                    State.UpdateImmersionLevelRaw(indication.Value);
                    State.UpdateEffectiveImmersionLevelRaw(indication.Value);
                }
                break;
            case HeadsetIndicationIdentifier.GenericModeEnabledChanged:
            case HeadsetIndicationIdentifier.CableChanged:
            case HeadsetIndicationIdentifier.AudioPromptFinished:
            case HeadsetIndicationIdentifier.TouchButtonPressed:
            case HeadsetIndicationIdentifier.TouchDial:
                break;
            default:
                break;
        }

        HeadsetIndicationReceived?.Invoke(this, new NuraHeadsetIndicationEventArgs(indication.Identifier, indication.Value));
    }

    private async Task StopMonitoringLoopAsync() {
        if (_monitoringCts is null) {
            return;
        }

        var monitoringSession = _monitoringSession;
        if (monitoringSession is not null) {
            monitoringSession.IndicationReceived -= HandleSessionIndication;
        }

        _monitoringSession = null;
        _monitoringIndications?.Writer.TryComplete();
        _monitoringIndications = null;
        _monitoringCts.Cancel();
        if (_monitoringTask is not null) {
            try {
                await _monitoringTask;
            } catch (OperationCanceledException) {
            }
        }

        _monitoringTask = null;
        _monitoringCts.Dispose();
        _monitoringCts = null;
        UpdateMonitoringState(false);
        _logger.Information(Source, $"Stopped monitoring loop for {Info.Serial}.");
    }

    private void HandleSessionIndication(GaiaResponse response) {
        _monitoringIndications?.Writer.TryWrite(response);
    }

    private void HandleSessionDisconnected(Exception exception) {
        var session = _session;
        if (session is null) {
            return;
        }

        _ = StopDisconnectedSessionAsync(session, exception);
    }

    private async Task StopDisconnectedSessionAsync(ConnectedNuraDeviceSession session, Exception exception) {
        try {
            await _sessionStopGate.WaitAsync();
            try {
                if (!ReferenceEquals(_session, session)) {
                    return;
                }

                _logger.Warning(Source, $"Local session disconnected for {Info.Serial}: {exception.Message}");
                await StopSessionCoreAsync();
            } finally {
                _sessionStopGate.Release();
            }
        } catch (Exception stopException) {
            _logger.Warning(Source, $"Failed to stop disconnected local session for {Info.Serial}: {stopException.Message}");
        }
    }

    private void UpdateLocalSessionState(bool hasLocalSession) {
        var previous = _hasLocalSession;
        _hasLocalSession = hasLocalSession;
        if (previous != hasLocalSession) {
            LocalSessionChanged?.Invoke(this, new NuraValueChangedEventArgs<bool>(previous, hasLocalSession));
            RaiseChanged();
        }
    }

    private void UpdateMonitoringState(bool isMonitoring) {
        var previous = _isMonitoring;
        _isMonitoring = isMonitoring;
        if (previous != isMonitoring) {
            MonitoringChanged?.Invoke(this, new NuraValueChangedEventArgs<bool>(previous, isMonitoring));
            RaiseChanged();
        }
    }

    private void RefreshProvisioningRequirement(bool raiseEvents) {
        UpdateProvisioningRequirement(CalculateProvisioningRequirementReason(), raiseEvents);
    }

    private NuraProvisioningRequirementReason CalculateProvisioningRequirementReason() {
        if (!HasPersistentDeviceKey) {
            return NuraProvisioningRequirementReason.MissingDeviceKey;
        }

        if (!IsNuraNowDevice) {
            return NuraProvisioningRequirementReason.None;
        }

        if (LastProvisionedUtc is null) {
            return NuraProvisioningRequirementReason.NuraNowRefreshRequired;
        }

        return LastProvisionedUtc.Value.Add(NuraNowProvisioningRefreshInterval) <= DateTimeOffset.UtcNow
            ? NuraProvisioningRequirementReason.NuraNowRefreshRequired
            : NuraProvisioningRequirementReason.None;
    }

    private void UpdateProvisioningRequirement(NuraProvisioningRequirementReason reason, bool raiseEvents) {
        var required = reason != NuraProvisioningRequirementReason.None;
        var previousRequired = _provisioningRequired;
        var previousReason = _provisioningRequirementReason;

        _provisioningRequired = required;
        _provisioningRequirementReason = reason;

        if (!raiseEvents) {
            return;
        }

        var changed = false;
        if (previousRequired != required) {
            ProvisioningRequiredChanged?.Invoke(this, new NuraValueChangedEventArgs<bool?>(previousRequired, required));
            changed = true;
        }

        if (previousReason != reason) {
            ProvisioningRequirementReasonChanged?.Invoke(this, new NuraValueChangedEventArgs<NuraProvisioningRequirementReason>(previousReason, reason));
            changed = true;
        }

        if (changed) {
            RaiseChanged();
        }
    }

    private void UpdateOperationStatus(
        NuraDeviceOperationKind kind,
        string stageCode,
        string message,
        bool isRunning,
        bool isCompleted,
        bool isError) {
        var next = new NuraDeviceOperationStatus(
            kind,
            stageCode,
            message,
            isRunning,
            isCompleted,
            isError,
            DateTimeOffset.UtcNow);
        var previous = _operationStatus;
        _operationStatus = next;
        if (!Equals(previous, next)) {
            OperationStatusChanged?.Invoke(this, new NuraValueChangedEventArgs<NuraDeviceOperationStatus?>(previous, next));
            RaiseChanged();
        }
    }

    private void UpdateProvisioningStage(string? stageCode) {
        var normalizedStage = string.IsNullOrWhiteSpace(stageCode) ? "session/start" : stageCode!;
        UpdateOperationStatus(
            NuraDeviceOperationKind.Provision,
            normalizedStage,
            GetProvisioningStageMessage(normalizedStage),
            isRunning: true,
            isCompleted: false,
            isError: false);
    }

    private static string GetProvisioningStageMessage(string stageCode) {
        return stageCode switch {
            "session/start" => "Starting provisioning.",
            "session/start_1" => "Provisioning stage session/start_1.",
            "session/start_2" => "Provisioning stage session/start_2.",
            "session/start_3" => "Provisioning stage session/start_3.",
            "session/start_4" => "Provisioning stage session/start_4.",
            _ => $"Provisioning stage {stageCode}."
        };
    }
}
