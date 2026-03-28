using NuraDesktopConsole.Library.Protocol;

namespace NuraDesktopConsole.Library.Transport;

internal interface IHeadsetTransport : IAsyncDisposable {
    string Describe();

    Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken);

    Task<GaiaResponse> SendAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken);

    Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GaiaResponse>> SendAndCollectAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GaiaResponse>> CollectAsync(
        Logging.SessionLogger logger,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken);
}
