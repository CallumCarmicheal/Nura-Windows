using NuraLib.Protocol;
using NuraLib.Crypto;
using NuraLib.Transport;

namespace NuraLib.Devices;

internal sealed class ConnectedNuraDeviceSession : IAsyncDisposable {
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ConnectedNuraDeviceSession(NuraSessionRuntime runtime, IHeadsetTransport transport) {
        Runtime = runtime;
        Transport = transport;
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
