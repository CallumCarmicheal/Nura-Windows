using NuraLib.Configuration;
using NuraLib.Crypto;
using NuraLib.Auth;
using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Utilities.Docs;
using NuraLib.Transport;

namespace NuraLib.Devices;

/// <summary>
/// Represents a Nura device that is currently connected and can participate in live operations.
/// </summary>
public sealed class ConnectedNuraDevice : NuraDevice {
    private const string Source = nameof(ConnectedNuraDevice);
    private readonly NuraConfigState _state;
    private readonly NuraAuthManager _authManager;
    private readonly NuraClientLogger _logger;
    private ConnectedNuraDeviceSession? _session;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    internal ConnectedNuraDevice(NuraConfigState state, NuraAuthManager authManager, NuraDeviceConfig config, NuraClientLogger logger) : base(config) {
        _state = state;
        _authManager = authManager;
        _logger = logger;
        State = new NuraDeviceState(this);
        Configuration = new NuraDeviceConfiguration(this);
        Profiles = new NuraProfiles(this);
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

    /// <summary>
    /// Raised when the headset emits an indication frame while monitoring is active.
    /// </summary>
    public event EventHandler<NuraHeadsetIndicationEventArgs>? HeadsetIndicationReceived;

    /// <summary>
    /// Determines whether the device still requires backend-assisted provisioning before local encrypted control can be used.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when no persistent device key is available; otherwise, <see langword="false"/>.
    /// </returns>
    public Task<bool> RequiresProvisioningAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!HasPersistentDeviceKey);
    }

    /// <summary>
    /// Ensures the device is provisioned and that a persistent device key has been recovered and stored.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A result describing whether provisioning succeeded and, if not, the known failure reason.
    /// </returns>
    public async Task<NuraProvisioningResult> EnsureProvisionedAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (HasPersistentDeviceKey) {
            return new NuraProvisioningResult(true);
        }

        if (!_authManager.HasStoredCredentials) {
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

            await using IHeadsetTransport transport = new RfcommHeadsetTransport(_logger);
            await transport.ConnectAsync(Info.DeviceAddress, cancellationToken);

            while (true) {
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

            var updatedDeviceConfig = Config with { DeviceKey = _authManager.CurrentAppEncKey };
            UpdateConfig(updatedDeviceConfig);
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
            return new NuraProvisioningResult(true);
        } catch (InvalidOperationException ex) when (!_authManager.HasStoredCredentials || ex.Message.Contains("stored authenticated session", StringComparison.OrdinalIgnoreCase)) {
            _logger.Warning(Source, $"Provisioning failed for {Info.Serial}: {ex.Message}");
            return new NuraProvisioningResult(false, NuraProvisioningError.NotAuthenticated);
        } catch (Exception ex) {
            _logger.Error(Source, $"Provisioning failed for {Info.Serial}: {ex.Message}", ex);
            return new NuraProvisioningResult(false, NuraProvisioningError.Unknown);
        }
    }

    /// <summary>
    /// Establishes a local encrypted control session to the connected device.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task ConnectLocalAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session is not null) {
            return;
        }

        if (!HasPersistentDeviceKey) {
            throw new InvalidOperationException($"device {Info.Serial} does not have a persistent device key");
        }

        var runtime = NuraSessionRuntime.Create(Config);
        var transport = new RfcommHeadsetTransport(_logger);
        await transport.ConnectAsync(Info.DeviceAddress, cancellationToken);
        await NuraLocalSessionSupport.PerformAppHandshakeAsync(runtime, transport, _logger, cancellationToken);
        _session = new ConnectedNuraDeviceSession(runtime, transport, _logger, Info.Serial);
        _logger.Information(Source, $"Local encrypted session established for {Info.Serial}.");

        if (Info.Supports(NuraSystemCapabilities.Profiles)) {
            var profileId = await NuraLocalSessionSupport.ReadCurrentProfileAsync(_session, _logger, cancellationToken);
            Profiles.UpdateProfileId(profileId);

            if (Info.Supports(NuraAudioCapabilities.Anc)) {
                try {
                    var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session, _logger, profileId, cancellationToken);
                    State.UpdateAnc(ancState);
                } catch (Exception ex) {
                    _logger.Debug(Source, $"Initial ANC read skipped for {Info.Serial}: {ex.Message}");
                }
            }
        }
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
        await RefreshProfilesAsync(cancellationToken);
        await RefreshConfigurationAsync(cancellationToken);
        await RefreshStateAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes the currently implemented live state values from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshStateAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);
        if (Info.Supports(NuraAudioCapabilities.Anc)) {
            await RetrieveAncStateAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.AncLevel)) {
            await RetrieveAncLevelAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.GlobalAncToggle)) {
            await RetrieveGlobalAncEnabledAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.PersonalisedMode)) {
            await RetrievePersonalisationModeAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.Immersion) && !UsesClassicKickitCommands()) {
            await RetrieveImmersionLevelAsync(cancellationToken);
        }

        if (Info.Supports(NuraAudioCapabilities.Spatial)) {
            await RetrieveSpatialEnabledAsync(cancellationToken);
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
        await RefreshProfileNamesAsync(3, cancellationToken);
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

        if (Info.Supports(NuraAudioCapabilities.Anc)) {
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
    }

    internal async Task<NuraAncState?> RetrieveAncStateAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session!, _logger, profileId, cancellationToken);
        State.UpdateAnc(ancState);
        return ancState;
    }

    internal async Task SetAncStateAsync(NuraAncState state, CancellationToken cancellationToken) {
        _ = state ?? throw new ArgumentNullException(nameof(state));
        await EnsureConnectedAsync(cancellationToken);
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        await NuraLocalSessionSupport.SetAncStateAsync(_session!, _logger, profileId, state, cancellationToken);
        State.UpdateAnc(state);
    }

    internal async Task SetAncEnabledAsync(bool enabled, CancellationToken cancellationToken) {
        var currentState = State.Anc ?? await RetrieveAncStateAsync(cancellationToken) ?? new NuraAncState();
        await SetAncStateAsync(currentState with { AncEnabled = enabled }, cancellationToken);
    }

    internal async Task SetPassthroughEnabledAsync(bool enabled, CancellationToken cancellationToken) {
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
        EnsureKickitStateTransportSupported();
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
        var kickitState = await NuraLocalSessionSupport.ReadKickitStateAsync(_session!, _logger, profileId, cancellationToken);
        ApplyKickitState(kickitState);
        return State.ImmersionLevel;
    }

    internal async Task SetImmersionLevelAsync(NuraImmersionLevel level, CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        EnsureKickitStateTransportSupported();
        var profileId = await RequireCurrentProfileIdAsync(cancellationToken);
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

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken) {
        if (_session is null) {
            await ConnectLocalAsync(cancellationToken);
        }
    }

    private async Task<int> RequireCurrentProfileIdAsync(CancellationToken cancellationToken) {
        return Profiles.ProfileId ?? await RetrieveCurrentProfileIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("current profile id is not available");
    }

    private bool UsesClassicKickitCommands() => Info.DeviceType == NuraDeviceType.Nuraphone;

    private bool RequiresTemporaryAncStateBeforeProfileSwitch() =>
        Info.DeviceType == NuraDeviceType.Nuraphone &&
        Info.FirmwareVersion >= 535;

    private void EnsureKickitStateTransportSupported() {
        if (UsesClassicKickitCommands()) {
            throw new NotImplementedException("Immersion level transport is not wired for classic Nuraphone kickit parameters yet.");
        }
    }

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

    private async Task StopSessionAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        await StopMonitoringLoopAsync();
        if (_session is null) {
            return;
        }

        await _session.DisposeAsync();
        _session = null;
        _logger.Information(Source, $"Local session stopped for {Info.Serial}.");
    }

    private async Task StartMonitoringCoreAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);

        if (_monitoringTask is not null && !_monitoringTask.IsCompleted) {
            return;
        }

        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _monitoringTask = RunMonitoringLoopAsync(_monitoringCts.Token);
        _logger.Information(Source, $"Started monitoring loop for {Info.Serial}.");
    }

    private async Task RunMonitoringLoopAsync(CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested && _session is not null) {
                var frames = await _session.CollectAsync(idleTimeoutMs: 1500, maxFrames: 8, cancellationToken);
                foreach (var frame in frames) {
                    var indication = HeadsetIndicationParser.Parse(frame);
                    if (indication is null) {
                        _logger.Trace(Source, $"rx.non_indication.command=0x{frame.CommandId:x4}");
                        continue;
                    }

                    HandleIndication(indication);
                }

                await Task.Delay(100, cancellationToken);
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
                break;
            case HeadsetIndicationIdentifier.AncParametersChanged:
                State.UpdateAnc(HeadsetIndicationParser.DecodeNuraphoneAncState(indication.Value));
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
                State.UpdateImmersionLevelRaw(indication.Value);
                State.UpdateEffectiveImmersionLevelRaw(indication.Value);
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
        _logger.Information(Source, $"Stopped monitoring loop for {Info.Serial}.");
    }
}
