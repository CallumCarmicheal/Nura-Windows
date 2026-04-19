using System.Buffers.Binary;
using System.Net.Sockets;

using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Utilities;

namespace NuraLib.Transport;

internal sealed class RfcommHeadsetTransport : IHeadsetTransport {
    private const string Source = nameof(RfcommHeadsetTransport);
    private const int AfBluetooth = 32;
    private const int BthProtoRfcomm = 3;
    private readonly NuraClientLogger _logger;
    private Socket? _socket;

    public RfcommHeadsetTransport(NuraClientLogger logger) {
        _logger = logger;
    }

    public string Describe()
        => "Windows Winsock RFCOMM transport using SPP UUID 00001101-0000-1000-8000-00805F9B34FB";

    public async Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken) {
        var bluetoothAddress = BluetoothAddress.Parse(deviceAddress);
        var endpoint = new BluetoothEndPoint(bluetoothAddress, BluetoothConstants.SerialPortServiceClassUuid);

        _socket = new Socket((AddressFamily)AfBluetooth, SocketType.Stream, (ProtocolType)BthProtoRfcomm);
        _socket.Bind(new BluetoothEndPoint(0, Guid.Empty, 0));
        _logger.Information(Source, $"Connecting RFCOMM transport to {deviceAddress}.");
        await _socket.ConnectAsync(endpoint, cancellationToken);
        _logger.Information(Source, $"Connected RFCOMM transport to {deviceAddress}.");
    }

    public async Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        CancellationToken cancellationToken) {
        await SendFrameAsync(frame, cancellationToken);

        while (true) {
            var response = await ReadResponseAsync(cancellationToken);
            if ((GaiaCommandId)response.CommandId == expectedResponse) {
                return response;
            }

            _logger.Debug(Source, $"Ignoring unexpected response command 0x{response.CommandId:x4} while waiting for 0x{(ushort)expectedResponse:x4}.");
        }
    }

    public async Task<GaiaResponse> SendAsync(GaiaFrame frame, CancellationToken cancellationToken) {
        await SendFrameAsync(frame, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GaiaResponse>> SendAndCollectAsync(
        GaiaFrame frame,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken) {
        if (maxFrames <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        }

        await SendFrameAsync(frame, cancellationToken);
        return await CollectAsync(idleTimeoutMs, maxFrames, cancellationToken);
    }

    public async Task<IReadOnlyList<GaiaResponse>> CollectAsync(
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken) {
        if (maxFrames <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        }

        var responses = new List<GaiaResponse>(maxFrames);
        while (responses.Count < maxFrames) {
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idleCts.CancelAfter(TimeSpan.FromMilliseconds(idleTimeoutMs));

            try {
                var response = await ReadResponseAsync(idleCts.Token);
                responses.Add(response);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                _logger.Debug(Source, "Frame collection stopped after idle timeout.");
                break;
            }
        }

        return responses;
    }

    public ValueTask DisposeAsync() {
        _socket?.Dispose();
        _socket = null;
        return ValueTask.CompletedTask;
    }

    private async Task SendFrameAsync(GaiaFrame frame, CancellationToken cancellationToken) {
        EnsureConnected();
        _logger.Trace(Source, $"tx.command=0x{(ushort)frame.CommandId:x4}");
        _logger.Trace(Source, $"tx.frame.hex={HexEncoding.Format(frame.Bytes)}");
        await _socket!.SendAsync(frame.Bytes, SocketFlags.None, cancellationToken);
    }

    private async Task<GaiaResponse> ReadResponseAsync(CancellationToken cancellationToken) {
        var bytes = await ReadFrameAsync(cancellationToken);
        _logger.Trace(Source, $"rx.frame.hex={HexEncoding.Format(bytes)}");
        var response = GaiaResponse.Parse(bytes);
        _logger.Debug(Source, response.ToString());
        return response;
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken) {
        var prefix = await ReadExactAsync(4, cancellationToken);
        var usesLengthExtension = (prefix[2] & 0x02) != 0;
        if (usesLengthExtension) {
            var lengthTail = await ReadExactAsync(5, cancellationToken);
            var header = prefix.Concat(lengthTail).ToArray();
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(3, 2));
            var remaining = await ReadExactAsync(length, cancellationToken);
            return header.Concat(remaining).ToArray();
        } else {
            var headerTail = await ReadExactAsync(4, cancellationToken);
            var header = prefix.Concat(headerTail).ToArray();
            var length = header[3];
            var remaining = await ReadExactAsync(length, cancellationToken);
            return header.Concat(remaining).ToArray();
        }
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken cancellationToken) {
        var output = new byte[count];
        var offset = 0;
        while (offset < count) {
            var loaded = await _socket!.ReceiveAsync(output.AsMemory(offset, count - offset), SocketFlags.None, cancellationToken);
            if (loaded == 0) {
                throw new IOException("Bluetooth socket closed while reading");
            }

            offset += loaded;
        }

        return output;
    }

    private void EnsureConnected() {
        if (_socket is null) {
            throw new InvalidOperationException("transport is not connected");
        }
    }
}
