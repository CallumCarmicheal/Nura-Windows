using NuraLib.Protocol;
using NuraLib.Crypto;
using NuraLib.Logging;
using NuraLib.Transport;

namespace NuraLib.Devices;

internal sealed class ConnectedNuraDeviceSession : IAsyncDisposable {
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly NuraClientLogger _logger;
    private readonly string _deviceSerial;
    private int _disposed;

    public ConnectedNuraDeviceSession(NuraSessionRuntime runtime, IHeadsetTransport transport, NuraClientLogger logger, string deviceSerial) {
        Runtime = runtime;
        Transport = transport;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deviceSerial = string.IsNullOrWhiteSpace(deviceSerial) ? "unknown" : deviceSerial;
        Transport.IndicationReceived += HandleTransportIndication;
        Transport.Disconnected += HandleTransportDisconnected;
    }

    public NuraSessionRuntime Runtime { get; }

    public IHeadsetTransport Transport { get; }

    public event Action<GaiaResponse>? IndicationReceived;

    public event Action<Exception>? Disconnected;

    public async Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        CancellationToken cancellationToken) {
        await EnterGateAsync(cancellationToken);
        try {
            return await Transport.ExchangeAsync(frame, expectedResponse, cancellationToken);
        } finally {
            _gate.Release();
        }
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        NuraBluetoothCommand<TResponse> command,
        CancellationToken cancellationToken) {
        await EnterGateAsync(cancellationToken);
        try {
            var encryptCounterBefore = Runtime.Crypto.EncryptCounter;
            var decryptCounterBefore = Runtime.Crypto.DecryptCounter;
            var frame = command.CreateFrame(Runtime);
            var encryptCounterAfter = Runtime.Crypto.EncryptCounter;
            if (_logger.IsEnabled(NuraLogLevel.Trace)) {
                _logger.Trace(
                    command.Source,
                    $"device={_deviceSerial} name={command.Name} {command.DescribeRequest(frame)} expected_rx=0x{(ushort)command.ExpectedResponseCommandId:x4} enc_before={encryptCounterBefore} enc_after={encryptCounterAfter} dec_before={decryptCounterBefore}");
            }

            var response = await Transport.ExchangeAsync(frame, command.ExpectedResponseCommandId, cancellationToken);
            if (_logger.IsEnabled(NuraLogLevel.Trace)) {
                _logger.Trace(
                    command.Source,
                    $"device={_deviceSerial} name={command.Name} {command.DescribeResponse(response)}");
            }

            var output = command.ParseResponse(Runtime, response);
            if (_logger.IsEnabled(NuraLogLevel.Trace)) {
                _logger.Trace(
                    command.Source,
                    $"device={_deviceSerial} name={command.Name} dec_after={Runtime.Crypto.DecryptCounter}");
            }

            return output;
        } finally {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) {
            return;
        }

        Transport.IndicationReceived -= HandleTransportIndication;
        Transport.Disconnected -= HandleTransportDisconnected;
        await Transport.DisposeAsync();
    }

    private async Task EnterGateAsync(CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        if (Volatile.Read(ref _disposed) == 0) {
            return;
        }

        _gate.Release();
        throw new ObjectDisposedException(nameof(ConnectedNuraDeviceSession));
    }

    private void HandleTransportIndication(GaiaResponse response) {
        IndicationReceived?.Invoke(response);
    }

    private void HandleTransportDisconnected(Exception exception) {
        Disconnected?.Invoke(exception);
    }
}
