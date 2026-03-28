using NuraDesktopConsole.Library.Protocol;

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace NuraDesktopConsole.Library.Transport;

internal sealed class RfcommHeadsetTransport : IHeadsetTransport {
    private const int AfBluetooth = 32;
    private const int BthProtoRfcomm = 3;
    private Socket? _socket;

    public string Describe()
        => "Windows Winsock RFCOMM transport using SPP UUID 00001101-0000-1000-8000-00805F9B34FB";

    public async Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken) {
        var bluetoothAddress = BluetoothAddress.Parse(deviceAddress);
        var endpoint = new BluetoothEndPoint(
            bluetoothAddress,
            BluetoothConstants.SerialPortServiceClassUuid);

        _socket = new Socket((AddressFamily)AfBluetooth, SocketType.Stream, (ProtocolType)BthProtoRfcomm);
        _socket.Bind(new BluetoothEndPoint(0, Guid.Empty, 0));
        await _socket.ConnectAsync(endpoint, cancellationToken);
    }

    public async Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken) {
        await SendFrameAsync(frame, logger, cancellationToken);

        while (true) {
            var response = await ReadResponseAsync(logger, cancellationToken);

            if ((GaiaCommandId)response.CommandId == expectedResponse) {
                return response;
            }

            logger.WriteLine($"rx.ignored=true command=0x{response.CommandId:x4}");
        }
    }

    public async Task<GaiaResponse> SendAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken) {
        await SendFrameAsync(frame, logger, cancellationToken);
        return await ReadResponseAsync(logger, cancellationToken);
    }

    public async Task<IReadOnlyList<GaiaResponse>> SendAndCollectAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken) {
        if (maxFrames <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        }

        await SendFrameAsync(frame, logger, cancellationToken);
        return await CollectAsync(logger, idleTimeoutMs, maxFrames, cancellationToken);
    }

    public async Task<IReadOnlyList<GaiaResponse>> CollectAsync(
        Logging.SessionLogger logger,
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
                var response = await ReadResponseAsync(logger, idleCts.Token);
                responses.Add(response);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                logger.WriteLine("rx.collect.idle_timeout=true");
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

    private async Task SendFrameAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken) {
        EnsureConnected();
        logger.WriteLine($"tx.command={frame.CommandId}");
        logger.WriteLine($"tx.frame.hex={Hex.Format(frame.Bytes)}");

        await _socket!.SendAsync(frame.Bytes, SocketFlags.None, cancellationToken);
    }

    private async Task<GaiaResponse> ReadResponseAsync(
        Logging.SessionLogger logger,
        CancellationToken cancellationToken) {
        var bytes = await ReadFrameAsync(cancellationToken);
        logger.WriteLine($"rx.frame.hex={Hex.Format(bytes)}");
        var response = GaiaResponse.Parse(bytes);
        logger.WriteLine(response.ToDisplayString());
        return response;
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken) {
        var prefix = await ReadExactAsync(4, cancellationToken);
        var usesLengthExtension = (prefix[2] & 0x02) != 0;
        if (usesLengthExtension) {
            var lengthTail = await ReadExactAsync(5, cancellationToken);
            var header = ByteArray.Combine(prefix, lengthTail);
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(3, 2));
            var remaining = await ReadExactAsync(length, cancellationToken);
            return ByteArray.Combine(header, remaining);
        } else {
            var headerTail = await ReadExactAsync(4, cancellationToken);
            var header = ByteArray.Combine(prefix, headerTail);
            var length = header[3];
            var remaining = await ReadExactAsync(length, cancellationToken);
            return ByteArray.Combine(header, remaining);
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
