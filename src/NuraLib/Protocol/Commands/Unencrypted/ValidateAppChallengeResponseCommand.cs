using NuraLib.Crypto;

namespace NuraLib.Protocol;

internal sealed class ValidateAppChallengeResponseCommand : NuraBluetoothCommand<byte[]> {
    private readonly NuraSessionRuntime _runtime;
    private readonly byte[] _gmac;

    public ValidateAppChallengeResponseCommand(NuraSessionRuntime runtime, byte[] gmac) {
        _runtime = runtime;
        _gmac = gmac ?? throw new ArgumentNullException(nameof(gmac));
    }

    public override string Name => "ValidateAppChallengeResponse";

    public override GaiaCommandId ExpectedResponseCommandId => GaiaCommandId.CryptoAppValidateChallengeResponse;

    public override GaiaFrame CreateFrame(NuraSessionRuntime? runtime = null) =>
        GaiaPacketFactory.CreateCommand(
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            [.. _runtime.Nonce, .. _gmac]);

    public override byte[] ParseResponse(NuraSessionRuntime? runtime, GaiaResponse response) => response.PayloadExcludingStatus;
}
