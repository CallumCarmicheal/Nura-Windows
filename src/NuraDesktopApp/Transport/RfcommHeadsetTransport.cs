using System.Net;
using System.Net.Sockets;

namespace desktop_app.Transport;

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
            Protocol.BluetoothConstants.SerialPortServiceClassUuid);

        _socket = new Socket((AddressFamily)AfBluetooth, SocketType.Stream, (ProtocolType)BthProtoRfcomm);
        _socket.Bind(new BluetoothEndPoint(0, Guid.Empty, 0));
        await _socket.ConnectAsync(endpoint, cancellationToken);
    }

    public async Task<desktop_app.Protocol.GaiaResponse> ExchangeAsync(
        desktop_app.Protocol.GaiaFrame frame,
        desktop_app.Protocol.GaiaCommandId expectedResponse,
        desktop_app.Logging.SessionLogger logger,
        CancellationToken cancellationToken) {
        EnsureConnected();
        logger.WriteLine($"tx.command={frame.CommandId}");
        logger.WriteLine($"tx.frame.hex={Hex.Format(frame.Bytes)}");

        await _socket!.SendAsync(frame.Bytes, SocketFlags.None, cancellationToken);

        while (true) {
            var bytes = await ReadFrameAsync(cancellationToken);
            logger.WriteLine($"rx.frame.hex={Hex.Format(bytes)}");
            var response = desktop_app.Protocol.GaiaResponse.Parse(bytes);
            logger.WriteLine(response.ToDisplayString());

            if ((desktop_app.Protocol.GaiaCommandId)response.CommandId == expectedResponse) {
                return response;
            }

            logger.WriteLine($"rx.ignored=true command=0x{response.CommandId:x4}");
        }
    }

    public ValueTask DisposeAsync() {
        _socket?.Dispose();
        _socket = null;
        return ValueTask.CompletedTask;
    }

    private async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken) {
        var header = await ReadExactAsync(8, cancellationToken);
        var length = header[3];
        if ((header[2] & 0x02) != 0) {
            throw new NotSupportedException("length-extension GAIA frames are not implemented yet");
        }

        var remaining = await ReadExactAsync(length, cancellationToken);
        return ByteArray.Combine(header, remaining);
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
