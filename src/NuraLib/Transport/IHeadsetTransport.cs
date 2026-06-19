using NuraLib.Protocol;

namespace NuraLib.Transport;

internal interface IHeadsetTransport : IAsyncDisposable {
    event Action<GaiaResponse>? IndicationReceived;

    event Action<Exception>? Disconnected;

    string Describe();

    Task ConnectAsync(string deviceAddress, CancellationToken cancellationToken);

    Task<GaiaResponse> SendAsync(GaiaFrame frame, CancellationToken cancellationToken);

    Task<GaiaResponse> ExchangeAsync(
        GaiaFrame frame,
        GaiaCommandId expectedResponse,
        CancellationToken cancellationToken);
}
