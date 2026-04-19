using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class GetDialConfigurationCommand : NuraAppEncryptedCommand<NuraDialConfiguration> {
    public GetDialConfigurationCommand(int profileId) {
        ProfileId = profileId;
    }

    public int ProfileId { get; }

    public override string Name => $"GetDialConfiguration(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [0x01, 0x06, checked((byte)ProfileId)];

    protected override NuraDialConfiguration ParsePlainPayload(byte[] plainPayload) =>
        NuraResponseParsers.DecodeDialConfiguration(plainPayload);
}
