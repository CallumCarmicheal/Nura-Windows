using NuraLib.Protocol;

namespace NuraLib.Transport;

internal interface IHeadsetTransport : IAsyncDisposable {
    string Describe();

    Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken);

    Task<GaiaResponse> SendAsync(GaiaFrame frame, CancellationToken cancellationToken);

    Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GaiaResponse>> SendAndCollectAsync(
        GaiaFrame frame,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GaiaResponse>> CollectAsync(
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken);
}
