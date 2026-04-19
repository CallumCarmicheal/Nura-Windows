using System.Net;
using System.Net.Sockets;

namespace NuraLib.Transport;

internal sealed class BluetoothEndPoint : EndPoint {
    private const int SockaddrBthSize = 30;

    public BluetoothEndPoint(ulong bluetoothAddress, Guid serviceClassId, uint port = 0) {
        BluetoothAddress = bluetoothAddress;
        ServiceClassId = serviceClassId;
        Port = port;
    }

    public ulong BluetoothAddress { get; }

    public Guid ServiceClassId { get; }

    public uint Port { get; }

    public override AddressFamily AddressFamily => (AddressFamily)32;

    public override SocketAddress Serialize() {
        var socketAddress = new SocketAddress(AddressFamily, SockaddrBthSize);

        var addressBytes = BitConverter.GetBytes(BluetoothAddress);
        for (var i = 0; i < addressBytes.Length; i++) {
            socketAddress[2 + i] = addressBytes[i];
        }

        var serviceBytes = ServiceClassId.ToByteArray();
        for (var i = 0; i < serviceBytes.Length; i++) {
            socketAddress[10 + i] = serviceBytes[i];
        }

        var portBytes = BitConverter.GetBytes(Port);
        for (var i = 0; i < portBytes.Length; i++) {
            socketAddress[26 + i] = portBytes[i];
        }

        return socketAddress;
    }

    public override EndPoint Create(SocketAddress socketAddress)
        => throw new NotSupportedException("BluetoothEndPoint.Create is not implemented");
}
