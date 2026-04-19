using NuraLib.Devices;

namespace NuraLib.Protocol;

internal static class NuraDialFunctionCodec {
    public static NuraDialFunction FromRawByte(byte value) =>
        value switch {
            0x00 => NuraDialFunction.None,
            0x01 => NuraDialFunction.Kickit,
            0x02 => NuraDialFunction.Anc,
            0x03 => NuraDialFunction.Volume,
            _ => NuraDialFunction.None
        };

    public static byte ToRawByte(NuraDialFunction function) =>
        function switch {
            NuraDialFunction.None => 0x00,
            NuraDialFunction.Kickit => 0x01,
            NuraDialFunction.Anc => 0x02,
            NuraDialFunction.Volume => 0x03,
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unsupported dial function.")
        };
}
