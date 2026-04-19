using NuraLib.Protocol;
using NuraLib.Crypto;
using NuraLib.Logging;
using NuraLib.Transport;

namespace NuraLib.Devices;

internal sealed class ConnectedNuraDeviceSession : IAsyncDisposable {
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly NuraClientLogger _logger;
    private readonly string _deviceSerial;

    public ConnectedNuraDeviceSession(NuraSessionRuntime runtime, IHeadsetTransport transport, NuraClientLogger logger, string deviceSerial) {
        Runtime = runtime;
        Transport = transport;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deviceSerial = string.IsNullOrWhiteSpace(deviceSerial) ? "unknown" : deviceSerial;
    }

    public NuraSessionRuntime Runtime { get; }

    public IHeadsetTransport Transport { get; }

    public async Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try {
            return await Transport.ExchangeAsync(frame, expectedResponse, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        NuraBluetoothCommand<TResponse> command,
        CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try {
            var frame = command.CreateFrame(Runtime);
            _logger.Trace(
                command.Source,
                $"device={_deviceSerial} name={command.Name} {command.DescribeRequest(frame)} expected_rx=0x{(ushort)command.ExpectedResponseCommandId:x4}");
            var response = await Transport.ExchangeAsync(frame, command.ExpectedResponseCommandId, cancellationToken);
            _logger.Trace(
                command.Source,
                $"device={_deviceSerial} name={command.Name} {command.DescribeResponse(response)}");
            return command.ParseResponse(Runtime, response);
        } finally {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<GaiaResponse>> CollectAsync(
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try {
            return await Transport.CollectAsync(idleTimeoutMs, maxFrames, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() {
        _gate.Dispose();
        await Transport.DisposeAsync();
    }
}
