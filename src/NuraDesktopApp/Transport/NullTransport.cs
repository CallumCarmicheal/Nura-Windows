namespace desktop_app.Transport;

internal sealed class NullTransport : IHeadsetTransport {
    public string Describe()
        => "no live transport yet; use this build to validate config, nonce generation, handshake GMAC, and GAIA packet bytes";

    public Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot connect");

    public Task<desktop_app.Protocol.GaiaResponse> ExchangeAsync(
        desktop_app.Protocol.GaiaFrame frame,
        desktop_app.Protocol.GaiaCommandId expectedResponse,
        desktop_app.Logging.SessionLogger logger,
        CancellationToken cancellationToken)
        => throw new NotSupportedException("Null transport cannot exchange frames");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
