using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class SetAncStateCommand : NuraAppEncryptedCommand<byte[]> {
    public SetAncStateCommand(int profileId, NuraAncState state) {
        ProfileId = profileId;
        State = state ?? throw new ArgumentNullException(nameof(state));
    }

    public int ProfileId { get; }

    public NuraAncState State { get; }

    public override string Name => $"SetANCState(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() {
        var ancEnabledRaw = State.AncEnabled ? (byte)0x01 : (byte)0x00;
        var passthroughEnabledRaw = State.PassthroughEnabled ? (byte)0x01 : (byte)0x00;
        return [0x00, 0x48, checked((byte)ProfileId), ancEnabledRaw, passthroughEnabledRaw];
    }

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
