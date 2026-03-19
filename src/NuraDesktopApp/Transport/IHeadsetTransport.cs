namespace desktop_app.Transport;

internal interface IHeadsetTransport : IAsyncDisposable {
    string Describe();

    Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken);

    Task<desktop_app.Protocol.GaiaResponse> ExchangeAsync(
        desktop_app.Protocol.GaiaFrame frame,
        desktop_app.Protocol.GaiaCommandId expectedResponse,
        desktop_app.Logging.SessionLogger logger,
        CancellationToken cancellationToken);
}
