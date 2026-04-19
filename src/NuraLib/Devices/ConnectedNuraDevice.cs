using NuraLib.Configuration;
using NuraLib.Crypto;
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
    private readonly NuraClientLogger _logger;
    private ConnectedNuraDeviceSession? _session;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;

    internal ConnectedNuraDevice(NuraDeviceConfig config, NuraClientLogger logger) : base(config) {
        _logger = logger;
        State = new NuraDeviceState(this);
        Configuration = new NuraDeviceConfiguration();
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

    [BluetoothImplementationRequired("Provisioning", Notes = "Needs full bootstrap flow through session/start_4 and persistent key storage.")]
    /// <summary>
    /// Ensures the device is provisioned and that a persistent device key has been recovered and stored.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// A result describing whether provisioning succeeded and, if not, the known failure reason.
    /// </returns>
    public Task<NuraProvisioningResult> EnsureProvisionedAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotImplementedException("Device provisioning has not been wired into NuraLib yet.");
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
        _session = new ConnectedNuraDeviceSession(runtime, transport);
        _logger.Information(Source, $"Local encrypted session established for {Info.Serial}.");

        var profileId = await NuraLocalSessionSupport.ReadCurrentProfileAsync(_session, _logger, cancellationToken);
        Profiles.UpdateProfileId(profileId);

        try {
            var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session, _logger, profileId, cancellationToken);
            State.UpdateAnc(ancState);
        } catch (Exception ex) {
            _logger.Debug(Source, $"Initial ANC read skipped for {Info.Serial}: {ex.Message}");
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
        await RefreshStateAsync(cancellationToken);
    }

    /// <summary>
    /// Refreshes the currently implemented live state values from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshStateAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);

        var profileId = Profiles.ProfileId ?? await RetrieveCurrentProfileIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("current profile id is not available");

        var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session!, _logger, profileId, cancellationToken);
        State.UpdateAnc(ancState);
    }

    /// <summary>
    /// Refreshes the currently implemented profile metadata from the headset.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RefreshProfilesAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureConnectedAsync(cancellationToken);
        await RetrieveCurrentProfileIdAsync(cancellationToken);
        await RefreshProfileNamesAsync(3, cancellationToken);
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

    internal async Task<NuraAncState?> RetrieveAncStateAsync(CancellationToken cancellationToken) {
        await EnsureConnectedAsync(cancellationToken);
        var profileId = Profiles.ProfileId ?? await RetrieveCurrentProfileIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("current profile id is not available");
        var ancState = await NuraLocalSessionSupport.ReadAncStateAsync(_session!, _logger, profileId, cancellationToken);
        State.UpdateAnc(ancState);
        return ancState;
    }

    internal async Task SetAncStateAsync(NuraAncState state, CancellationToken cancellationToken) {
        _ = state ?? throw new ArgumentNullException(nameof(state));
        await EnsureConnectedAsync(cancellationToken);
        var profileId = Profiles.ProfileId ?? await RetrieveCurrentProfileIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("current profile id is not available");
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

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken) {
        if (_session is null) {
            await ConnectLocalAsync(cancellationToken);
        }
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
                State.UpdateImmersionEnabled(indication.Value != 0);
                break;
            case HeadsetIndicationIdentifier.AncLevelChanged:
                State.UpdateAncLevel(indication.Value);
                break;
            case HeadsetIndicationIdentifier.KickitLevelChanged:
                State.UpdateImmersionLevel(indication.Value);
                State.UpdateEffectiveImmersionLevel(indication.Value);
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
