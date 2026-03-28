using NuraDesktopConsole.Library.Protocol;

namespace NuraDesktopConsole.Library.Transport;

internal sealed class NullTransport : IHeadsetTransport {
    public string Describe()
        => "no live transport yet; use this build to validate config, nonce generation, handshake GMAC, and GAIA packet bytes";

    public Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot connect");

    public Task<GaiaResponse> SendAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot send frames");

    public Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        Logging.SessionLogger logger,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot exchange frames");

    public Task<IReadOnlyList<GaiaResponse>> SendAndCollectAsync(
        GaiaFrame frame,
        Logging.SessionLogger logger,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot collect frames");

    public Task<IReadOnlyList<GaiaResponse>> CollectAsync(
        Logging.SessionLogger logger,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot collect frames");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
