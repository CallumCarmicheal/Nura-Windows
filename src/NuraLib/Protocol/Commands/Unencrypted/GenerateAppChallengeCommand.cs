namespace NuraLib.Protocol;

internal sealed class GenerateAppChallengeCommand : NuraUnencryptedCommand<byte[]> {
    public override string Name => "GenerateAppChallenge";

    public override GaiaCommandId ExpectedResponseCommandId => GaiaCommandId.CryptoAppGenerateChallenge;

    protected override GaiaFrame CreateFrameCore() =>
        GaiaPacketFactory.CreateCommand(GaiaCommandId.CryptoAppGenerateChallenge);

    protected override byte[] ParseResponseCore(GaiaResponse response) => response.PayloadExcludingStatus;
}
