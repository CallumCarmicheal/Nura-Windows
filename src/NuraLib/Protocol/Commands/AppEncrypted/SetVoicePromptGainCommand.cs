using NuraLib.Devices;

namespace NuraLib.Protocol;

internal sealed class SetVoicePromptGainCommand : NuraAppEncryptedCommand<byte[]> {
    public SetVoicePromptGainCommand(NuraVoicePromptGain gain) {
        Gain = gain;
    }

    public NuraVoicePromptGain Gain { get; }

    public override string Name => $"SetVoicePromptGain(gain={Gain})";

    protected override byte[] CreatePlainPayload() => [
        0x01,
        0x76,
        NuraVoicePromptGainCodec.ToPayloadByte(Gain)
    ];

    protected override byte[] ParsePlainPayload(byte[] plainPayload) => plainPayload;
}
