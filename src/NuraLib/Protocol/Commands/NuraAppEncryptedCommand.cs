using NuraLib.Crypto;

namespace NuraLib.Protocol;

internal abstract class NuraAppEncryptedCommand<TResponse> : NuraBluetoothCommand<TResponse> {
    public sealed override GaiaCommandId ExpectedResponseCommandId => GaiaCommandId.ResponseAppEncryptedAuthenticated;

    protected virtual GaiaCommandId RequestCommandId => GaiaCommandId.EntryAppEncryptedAuthenticated;

    public sealed override GaiaFrame CreateFrame(NuraSessionRuntime? runtime = null) {
        if (runtime is null) {
            throw new InvalidOperationException($"{Name} requires an active session runtime.");
        }

        return GaiaPacketFactory.CreateAuthenticatedAppCommand(runtime.Crypto, CreatePlainPayload());
    }

    public sealed override TResponse ParseResponse(NuraSessionRuntime? runtime, GaiaResponse response) {
        if (runtime is null) {
            throw new InvalidOperationException($"{Name} requires an active session runtime.");
        }

        if (response.Status != 0x00 || (GaiaCommandId)response.CommandId != ExpectedResponseCommandId) {
            throw new GaiaCommandException(Name, RequestCommandId, ExpectedResponseCommandId, response);
        }

        var plainPayload = NuraResponseParsers.DecryptAuthenticatedPlainPayload(runtime, response);
        return ParsePlainPayload(plainPayload);
    }

    protected abstract byte[] CreatePlainPayload();

    protected abstract TResponse ParsePlainPayload(byte[] plainPayload);
}
