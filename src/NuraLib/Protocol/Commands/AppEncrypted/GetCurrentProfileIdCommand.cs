namespace NuraLib.Protocol;

internal sealed class GetCurrentProfileIdCommand : NuraAppEncryptedCommand<int> {
    public override string Name => "GetCurrentProfileID";

    protected override byte[] CreatePlainPayload() => [0x00, 0x41];

    protected override int ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeCurrentProfileId(plainPayload);
}
