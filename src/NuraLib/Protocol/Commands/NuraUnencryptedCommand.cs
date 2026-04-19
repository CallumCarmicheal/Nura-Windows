using NuraLib.Crypto;

namespace NuraLib.Protocol;

internal abstract class NuraUnencryptedCommand<TResponse> : NuraBluetoothCommand<TResponse> {
    public sealed override GaiaFrame CreateFrame(NuraSessionRuntime? runtime = null) => CreateFrameCore();

    public sealed override TResponse ParseResponse(NuraSessionRuntime? runtime, GaiaResponse response) => ParseResponseCore(response);

    protected abstract GaiaFrame CreateFrameCore();

    protected abstract TResponse ParseResponseCore(GaiaResponse response);
}
