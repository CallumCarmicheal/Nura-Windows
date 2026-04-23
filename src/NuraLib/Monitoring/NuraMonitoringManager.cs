using NuraLib.Devices;
using NuraLib.Logging;

namespace NuraLib.Monitoring;

/// <summary>
/// Provides connection-level monitoring for Nura devices.
/// </summary>
public sealed class NuraMonitoringManager {
    private const string Source = nameof(NuraMonitoringManager);
    private readonly NuraDeviceManager _devices;
    private readonly NuraClientLogger _logger;
    private CancellationTokenSource? _pollingCts;
    private Task? _pollingTask;
    private Dictionary<string, ConnectedNuraDevice> _knownConnected = new(StringComparer.OrdinalIgnoreCase);

    internal NuraMonitoringManager(NuraDeviceManager devices, NuraClientLogger logger) {
        _devices = devices;
        _logger = logger;
    }

    /// <summary>
    /// Raised when a device becomes connected and visible to the monitoring layer.
    /// </summary>
    public event EventHandler<NuraDeviceConnectionEventArgs>? DeviceConnected;

    /// <summary>
    /// Raised when a previously connected device disconnects.
    /// </summary>
    public event EventHandler<NuraDeviceConnectionEventArgs>? DeviceDisconnected;

    /// <summary>
    /// Starts monitoring Bluetooth connection lifecycle events for Nura devices.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pollingTask is not null && !_pollingTask.IsCompleted) {
            return;
        }

        await _devices.RefreshAsync(cancellationToken);
        _knownConnected = _devices.Connected.ToDictionary(
            GetIdentityKey,
            StringComparer.OrdinalIgnoreCase);
        foreach (var device in _knownConnected.Values) {
            RaiseDeviceConnected(device);
        }

        _pollingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pollingTask = RunPollingLoopAsync(_pollingCts.Token);
        _logger.Information(Source, "Started Bluetooth connection monitoring.");
    }

    /// <summary>
    /// Stops connection lifecycle monitoring.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public Task StopAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pollingCts is null) {
            return Task.CompletedTask;
        }

        _pollingCts.Cancel();
        return AwaitStoppedAsync();
    }

    internal void RaiseDeviceConnected(Devices.ConnectedNuraDevice device) {
        DeviceConnected?.Invoke(this, new NuraDeviceConnectionEventArgs(device));
    }

    internal void RaiseDeviceDisconnected(Devices.ConnectedNuraDevice device) {
        DeviceDisconnected?.Invoke(this, new NuraDeviceConnectionEventArgs(device));
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await _devices.RefreshAsync(cancellationToken);
                ReconcileConnectedDevices(_devices.Connected);
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        } catch (Exception ex) {
            _logger.Warning(Source, $"Bluetooth monitoring loop ended with error: {ex.Message}");
        }
    }

    private void ReconcileConnectedDevices(IReadOnlyList<ConnectedNuraDevice> connectedDevices) {
        var next = connectedDevices.ToDictionary(
            GetIdentityKey,
            StringComparer.OrdinalIgnoreCase);

        foreach (var device in next.Values) {
            if (!_knownConnected.ContainsKey(GetIdentityKey(device))) {
                RaiseDeviceConnected(device);
            }
        }

        foreach (var previous in _knownConnected.Values) {
            if (!next.ContainsKey(GetIdentityKey(previous))) {
                RaiseDeviceDisconnected(previous);
            }
        }

        _knownConnected = next;
    }

    private async Task AwaitStoppedAsync() {
        try {
            if (_pollingTask is not null) {
                await _pollingTask;
            }
        } catch (OperationCanceledException) {
        } finally {
            _pollingTask = null;
            _pollingCts?.Dispose();
            _pollingCts = null;
            _logger.Information(Source, "Stopped Bluetooth connection monitoring.");
        }
    }

    private static string GetIdentityKey(ConnectedNuraDevice device) {
        var serial = device.Info.Serial?.Trim() ?? string.Empty;
        var address = device.Info.DeviceAddress?.Trim() ?? string.Empty;
        return $"{serial}|{address}";
    }
}
