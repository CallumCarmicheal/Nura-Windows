using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class GetAncStateCommand : NuraAppEncryptedCommand<NuraAncState> {
    public GetAncStateCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetANCState(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x00, 0x49, checked((byte)ProfileId)];

    protected override NuraAncState ParsePlainPayload(byte[] plainPayload) => NuraResponseParsers.DecodeAncState(plainPayload);
}
