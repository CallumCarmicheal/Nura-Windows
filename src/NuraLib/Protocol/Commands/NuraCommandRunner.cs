using NuraLib.Crypto;
using NuraLib.Transport;

namespace NuraLib.Protocol;

internal static class NuraCommandRunner {
    public static async Task<TResponse> ExecuteAsync<TResponse>(
        this IHeadsetTransport transport,
        NuraBluetoothCommand<TResponse> command,
        NuraSessionRuntime? runtime,
        CancellationToken cancellationToken) {
        var frame = command.CreateFrame(runtime);
        var response = await transport.ExchangeAsync(frame, command.ExpectedResponseCommandId, cancellationToken);
        return command.ParseResponse(runtime, response);
    }
}
