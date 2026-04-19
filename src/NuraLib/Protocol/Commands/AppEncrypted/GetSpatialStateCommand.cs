namespace NuraLib.Protocol;

internal sealed class GetSpatialStateCommand : NuraAppEncryptedCommand<bool> {
    public override string Name => "GetSpatialState";

    protected override byte[] CreatePlainPayload() => [0x01, 0x7a];

    protected override bool ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeBooleanFlag(plainPayload);
}
