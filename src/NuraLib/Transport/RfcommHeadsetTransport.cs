using System.Diagnostics;
using System.Net.Sockets;

using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Utilities;

namespace NuraLib.Transport;

internal sealed class RfcommHeadsetTransport : IHeadsetTransport {
    private const string Source = nameof(RfcommHeadsetTransport);
    private readonly NuraClientLogger _logger;
    private readonly Func<IRfcommDuplexConnection> _connectionFactory;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _sync = new();
    private IRfcommDuplexConnection? _connection;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private PendingRequest? _pendingRequest;
    private int _disposed;
    private bool _disconnectRaised;

    public RfcommHeadsetTransport(NuraClientLogger logger) : this(logger, static () => new WinsockRfcommDuplexConnection()) {
    }

    internal RfcommHeadsetTransport(NuraClientLogger logger, Func<IRfcommDuplexConnection> connectionFactory) {
        _logger = logger;
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public event Action<GaiaResponse>? IndicationReceived;

    public event Action<Exception>? Disconnected;

    public string Describe()
        => "Windows Winsock RFCOMM transport using SPP UUID 00001101-0000-1000-8000-00805F9B34FB";

    public async Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        if (_connection is not null) {
            throw new InvalidOperationException("RFCOMM transport is already connected.");
        }

        var connection = _connectionFactory();
        _logger.Information(Source, $"Connecting RFCOMM transport to {deviceAddress}.");
        try {
            await connection.ConnectAsync(deviceAddress, cancellationToken).ConfigureAwait(false);
        } catch {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _connection = connection;
        _receiveCts = new CancellationTokenSource();
        _receiveTask = ReceiveLoopAsync(connection, _receiveCts.Token);
        _logger.Information(Source, $"Connected RFCOMM transport to {deviceAddress}.");
    }

    public async Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        CancellationToken cancellationToken) {
        return await SendAndAwaitAsync(frame, expectedResponse, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GaiaResponse> SendAsync(GaiaFrame frame, CancellationToken cancellationToken) {
        return await SendAndAwaitAsync(frame, expectedResponse: null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() {
        IRfcommDuplexConnection? connection;
        CancellationTokenSource? receiveCts;
        Task? receiveTask;
        PendingRequest? pendingRequest;

        lock (_sync) {
            if (Volatile.Read(ref _disposed) != 0) {
                return;
            }

            Volatile.Write(ref _disposed, 1);
            connection = _connection;
            _connection = null;
            receiveCts = _receiveCts;
            _receiveCts = null;
            receiveTask = _receiveTask;
            _receiveTask = null;
            pendingRequest = _pendingRequest;
            _pendingRequest = null;
        }

        pendingRequest?.Completion.TrySetException(new ObjectDisposedException(nameof(RfcommHeadsetTransport)));
        receiveCts?.Cancel();
        if (connection is not null) {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        if (receiveTask is not null) {
            try {
                await receiveTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        receiveCts?.Dispose();
    }

    private async Task<GaiaResponse> SendAndAwaitAsync(
        GaiaFrame frame,
        GaiaCommandId? expectedResponse,
        CancellationToken cancellationToken) {
        var queueStarted = Stopwatch.GetTimestamp();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var queueWait = Stopwatch.GetElapsedTime(queueStarted);
        PendingRequest? pendingRequest = null;
        var exchangeStarted = Stopwatch.GetTimestamp();

        try {
            var connection = GetConnectedConnection();
            pendingRequest = new PendingRequest(
                frame.CommandId,
                expectedResponse,
                new TaskCompletionSource<GaiaResponse>(TaskCreationOptions.RunContinuationsAsynchronously));

            lock (_sync) {
                ThrowIfDisposed();
                if (_pendingRequest is not null) {
                    throw new InvalidOperationException("A Bluetooth request is already pending.");
                }

                _pendingRequest = pendingRequest;
            }

            if (_logger.IsEnabled(NuraLogLevel.Trace)) {
                _logger.Trace(
                    Source,
                    $"tx.command=0x{(ushort)frame.CommandId:x4} queue_wait_ms={queueWait.TotalMilliseconds:F1} frame.hex={HexEncoding.Format(frame.Bytes)}");
            }

            try {
                await SendAllAsync(connection, frame.Bytes, cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                FailTransport(ex);
                throw;
            }

            var response = await pendingRequest.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (_logger.IsEnabled(NuraLogLevel.Trace)) {
                var exchangeDuration = Stopwatch.GetElapsedTime(exchangeStarted);
                _logger.Trace(Source, $"exchange.complete command=0x{(ushort)frame.CommandId:x4} duration_ms={exchangeDuration.TotalMilliseconds:F1}");
            }

            return response;
        } finally {
            if (pendingRequest is not null) {
                lock (_sync) {
                    if (ReferenceEquals(_pendingRequest, pendingRequest)) {
                        _pendingRequest = null;
                    }
                }
            }

            _requestGate.Release();
        }
    }

    private async Task SendAllAsync(
        IRfcommDuplexConnection connection,
        byte[] bytes,
        CancellationToken cancellationToken) {
        var offset = 0;
        while (offset < bytes.Length) {
            var sent = await connection.SendAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (sent <= 0) {
                throw new IOException("Bluetooth socket closed while writing.");
            }

            offset += sent;
        }
    }

    private async Task ReceiveLoopAsync(IRfcommDuplexConnection connection, CancellationToken cancellationToken) {
        var receiveBuffer = new byte[1024];
        var bufferedBytes = new List<byte>();

        try {
            while (!cancellationToken.IsCancellationRequested) {
                var received = await connection.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);
                if (received == 0) {
                    throw new IOException("Bluetooth socket closed while reading.");
                }

                bufferedBytes.AddRange(receiveBuffer.AsSpan(0, received).ToArray());
                while (TryReadFrame(bufferedBytes, out var frameBytes)) {
                    DispatchIncomingFrame(frameBytes);
                }
            }
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        } catch (Exception ex) {
            _logger.Warning(Source, $"RFCOMM receive loop ended: {ex.Message}");
            FailTransport(ex);
        }
    }

    private bool TryReadFrame(List<byte> bufferedBytes, out byte[] frameBytes) {
        frameBytes = Array.Empty<byte>();

        var firstSofIndex = bufferedBytes.IndexOf(0xff);
        if (firstSofIndex < 0) {
            if (bufferedBytes.Count > 0 && _logger.IsEnabled(NuraLogLevel.Debug)) {
                _logger.Debug(Source, $"Discarded {bufferedBytes.Count} non-GAIA bytes while waiting for SOF.");
            }

            bufferedBytes.Clear();
            return false;
        }

        if (firstSofIndex > 0) {
            if (_logger.IsEnabled(NuraLogLevel.Debug)) {
                _logger.Debug(Source, $"Discarded {firstSofIndex} non-GAIA bytes before SOF.");
            }

            bufferedBytes.RemoveRange(0, firstSofIndex);
        }

        if (bufferedBytes.Count < 4) {
            return false;
        }

        var usesLengthExtension = (bufferedBytes[2] & 0x02) != 0;
        var headerLength = usesLengthExtension ? 9 : 8;
        if (bufferedBytes.Count < headerLength) {
            return false;
        }

        var payloadLength = usesLengthExtension
            ? (bufferedBytes[3] << 8) | bufferedBytes[4]
            : bufferedBytes[3];
        var totalLength = headerLength + payloadLength;
        if (bufferedBytes.Count < totalLength) {
            return false;
        }

        frameBytes = bufferedBytes.GetRange(0, totalLength).ToArray();
        bufferedBytes.RemoveRange(0, totalLength);
        return true;
    }

    private void DispatchIncomingFrame(byte[] frameBytes) {
        if (_logger.IsEnabled(NuraLogLevel.Trace)) {
            _logger.Trace(Source, $"rx.frame.hex={HexEncoding.Format(frameBytes)}");
        }

        GaiaResponse response;
        try {
            response = GaiaResponse.Parse(frameBytes);
        } catch (Exception ex) {
            _logger.Warning(Source, $"Discarded malformed GAIA frame: {ex.Message}");
            return;
        }

        if (_logger.IsEnabled(NuraLogLevel.Debug)) {
            _logger.Debug(Source, response.ToString());
        }

        if ((GaiaCommandId)response.CommandId == GaiaCommandId.IndicationFromHeadset) {
            DispatchIndication(response);
            return;
        }

        PendingRequest? pendingRequest;
        lock (_sync) {
            pendingRequest = _pendingRequest;
        }

        if (pendingRequest is null) {
            if (_logger.IsEnabled(NuraLogLevel.Debug)) {
                _logger.Debug(Source, $"Ignoring response command 0x{response.CommandId:x4} with no pending request.");
            }

            return;
        }

        var isExpectedResponse = pendingRequest.ExpectedResponse is null ||
            (GaiaCommandId)response.CommandId == pendingRequest.ExpectedResponse;
        var isSameCommandError = (GaiaCommandId)response.CommandId == pendingRequest.RequestCommand && response.Status != 0x00;
        if (isExpectedResponse || isSameCommandError) {
            if (isSameCommandError && _logger.IsEnabled(NuraLogLevel.Debug)) {
                _logger.Debug(
                    Source,
                    $"Received same-command error response 0x{response.CommandId:x4} status=0x{response.Status:x2} while waiting for 0x{(ushort?)pendingRequest.ExpectedResponse:x4}.");
            }

            pendingRequest.Completion.TrySetResult(response);
            return;
        }

        if (_logger.IsEnabled(NuraLogLevel.Debug)) {
            _logger.Debug(Source, $"Ignoring unexpected response command 0x{response.CommandId:x4} while waiting for 0x{(ushort)pendingRequest.ExpectedResponse!:x4}.");
        }
    }

    private void DispatchIndication(GaiaResponse response) {
        var handler = IndicationReceived;
        if (handler is null) {
            return;
        }

        try {
            handler.Invoke(response);
        } catch (Exception ex) {
            _logger.Warning(Source, $"Indication handler failed: {ex.Message}");
        }
    }

    private void FailTransport(Exception exception) {
        PendingRequest? pendingRequest;
        Action<Exception>? disconnected;

        lock (_sync) {
            if (_disconnectRaised || Volatile.Read(ref _disposed) != 0) {
                return;
            }

            _disconnectRaised = true;
            pendingRequest = _pendingRequest;
            disconnected = Disconnected;
        }

        pendingRequest?.Completion.TrySetException(exception);
        try {
            disconnected?.Invoke(exception);
        } catch (Exception callbackException) {
            _logger.Warning(Source, $"Disconnect handler failed: {callbackException.Message}");
        }
    }

    private IRfcommDuplexConnection GetConnectedConnection() {
        ThrowIfDisposed();
        if (_connection is null) {
            throw new InvalidOperationException("transport is not connected");
        }

        return _connection;
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0) {
            throw new ObjectDisposedException(nameof(RfcommHeadsetTransport));
        }
    }

    private sealed record PendingRequest(
        GaiaCommandId RequestCommand,
        GaiaCommandId? ExpectedResponse,
        TaskCompletionSource<GaiaResponse> Completion);

    private sealed class WinsockRfcommDuplexConnection : IRfcommDuplexConnection {
        private const int AfBluetooth = 32;
        private const int BthProtoRfcomm = 3;
        private Socket? _socket;

        public async Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken) {
            var bluetoothAddress = BluetoothAddress.Parse(deviceAddress);
            var endpoint = new BluetoothEndPoint(bluetoothAddress, BluetoothConstants.SerialPortServiceClassUuid);
            var socket = new Socket((AddressFamily)AfBluetooth, SocketType.Stream, (ProtocolType)BthProtoRfcomm);
            socket.Bind(new BluetoothEndPoint(0, Guid.Empty, 0));
            try {
                await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                _socket = socket;
            } catch {
                socket.Dispose();
                throw;
            }
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) {
            return GetSocket().ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
        }

        public ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) {
            return GetSocket().SendAsync(buffer, SocketFlags.None, cancellationToken);
        }

        public ValueTask DisposeAsync() {
            _socket?.Dispose();
            _socket = null;
            return ValueTask.CompletedTask;
        }

        private Socket GetSocket() {
            return _socket ?? throw new InvalidOperationException("RFCOMM socket is not connected.");
        }
    }
}
