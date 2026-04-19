using NuraLib.Devices;

namespace NuraLib.Protocol;

internal static class NuraVoicePromptGainCodec {
    public static sbyte ToGainDb(NuraVoicePromptGain gain) =>
        gain switch {
            NuraVoicePromptGain.Low => -20,
            NuraVoicePromptGain.Medium => 0,
            NuraVoicePromptGain.High => 20,
            _ => throw new ArgumentOutOfRangeException(nameof(gain), gain, "Unsupported voice prompt gain preset.")
        };

    public static byte ToPayloadByte(NuraVoicePromptGain gain) => unchecked((byte)ToGainDb(gain));
}
