using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class GetKickitEnabledCommand : NuraAppEncryptedCommand<NuraPersonalisationMode> {
    public override string Name => "GetKickitEnabled";

    protected override byte[] CreatePlainPayload() => [0x00, 0xb4];

    protected override NuraPersonalisationMode ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeBooleanFlag(plainPayload)
            ? NuraPersonalisationMode.Personalised
            : NuraPersonalisationMode.Neutral;
}
