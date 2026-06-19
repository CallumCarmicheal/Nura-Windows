namespace NuraLib.Transport;

// Separates the framed protocol dispatcher from Winsock so it can be tested deterministically.
internal interface IRfcommDuplexConnection : IAsyncDisposable {
    Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken);

    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
}
