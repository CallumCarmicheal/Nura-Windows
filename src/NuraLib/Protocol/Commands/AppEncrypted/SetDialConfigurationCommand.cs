using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class SetDialConfigurationCommand : NuraAppEncryptedCommand<byte[]> {
    public SetDialConfigurationCommand(int profileId, NuraDialConfiguration configuration) {
        ProfileId = profileId;
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public int ProfileId { get; }

    public NuraDialConfiguration Configuration { get; }

    public override string Name => $"SetDialConfiguration(profile={ProfileId})";

    protected override byte[] CreatePlainPayload() => [
        0x01,
        0x05,
        checked((byte)ProfileId),
        NuraDialFunctionCodec.ToRawByte(Configuration.Left),
        NuraDialFunctionCodec.ToRawByte(Configuration.Right)
    ];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
