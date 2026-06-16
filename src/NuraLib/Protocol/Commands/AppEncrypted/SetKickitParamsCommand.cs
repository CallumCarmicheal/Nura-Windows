using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class SetKickitParamsCommand : NuraAppEncryptedCommand<byte[]> {
    public SetKickitParamsCommand(int profileId, NuraImmersionLevel level) {
        ProfileId = profileId;
        Level = level;
    }

    public int ProfileId { get; }

    public NuraImmersionLevel Level { get; }

    public override string Name => $"SetKickitParams(profile={ProfileId}, level={Level})";

    protected override byte[] CreatePlainPayload() {
        var parameters = NuraClassicKickitParams.FromImmersionLevel(Level);
        return [0x00, 0x4c, checked((byte)ProfileId), parameters.DrcRaw, parameters.LpfRaw, parameters.GainRaw];
    }

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
