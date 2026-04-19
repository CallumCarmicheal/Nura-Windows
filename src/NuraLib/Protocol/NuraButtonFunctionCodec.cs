using NuraLib.Devices;

namespace NuraLib.Protocol;

internal static class NuraButtonFunctionCodec {
    public static NuraButtonFunction FromRawByte(byte value) =>
        value switch {
            0x00 => NuraButtonFunction.None,
            0x01 => NuraButtonFunction.PlayPauseAndCall,
            0x02 => NuraButtonFunction.PlayPauseOnly,
            0x03 => NuraButtonFunction.ToggleKickIt,
            0x04 => NuraButtonFunction.HoldForPassthroughOnOneSide,
            0x05 => NuraButtonFunction.HoldForPassthroughOnBothSides,
            0x06 => NuraButtonFunction.TogglePassthroughOnOneSide,
            0x07 => NuraButtonFunction.TogglePassthroughOnBothSides,
            0x08 => NuraButtonFunction.ToggleSocial,
            0x09 => NuraButtonFunction.ToggleGenericModeEnabled,
            0x0A => NuraButtonFunction.PreviousTrack,
            0x0B => NuraButtonFunction.NextTrack,
            0x0C => NuraButtonFunction.VolumeUp,
            0x0D => NuraButtonFunction.VolumeDown,
            0x0E => NuraButtonFunction.ToggleAnc,
            0x0F => NuraButtonFunction.CycleAncPassthrough,
            0x10 => NuraButtonFunction.SpeakBatteryLevel,
            0x11 => NuraButtonFunction.RejectCall,
            0x12 => NuraButtonFunction.PlayPauseAndAnswerCall,
            0x13 => NuraButtonFunction.VoiceAssistant,
            0x14 => NuraButtonFunction.Mute,
            0x15 => NuraButtonFunction.TogglePassthroughAndPause,
            0x16 => NuraButtonFunction.KickItUp,
            0x17 => NuraButtonFunction.KickItDown,
            0x18 => NuraButtonFunction.ToggleSpatial,
            0x19 => NuraButtonFunction.ToggleGamingMode,
            _ => NuraButtonFunction.Unknown
        };

    public static byte ToRawByte(NuraButtonFunction function) =>
        function switch {
            NuraButtonFunction.None => 0x00,
            NuraButtonFunction.PlayPauseAndCall => 0x01,
            NuraButtonFunction.PlayPauseOnly => 0x02,
            NuraButtonFunction.ToggleKickIt => 0x03,
            NuraButtonFunction.HoldForPassthroughOnOneSide => 0x04,
            NuraButtonFunction.HoldForPassthroughOnBothSides => 0x05,
            NuraButtonFunction.TogglePassthroughOnOneSide => 0x06,
            NuraButtonFunction.TogglePassthroughOnBothSides => 0x07,
            NuraButtonFunction.ToggleSocial => 0x08,
            NuraButtonFunction.ToggleGenericModeEnabled => 0x09,
            NuraButtonFunction.PreviousTrack => 0x0A,
            NuraButtonFunction.NextTrack => 0x0B,
            NuraButtonFunction.VolumeUp => 0x0C,
            NuraButtonFunction.VolumeDown => 0x0D,
            NuraButtonFunction.ToggleAnc => 0x0E,
            NuraButtonFunction.CycleAncPassthrough => 0x0F,
            NuraButtonFunction.SpeakBatteryLevel => 0x10,
            NuraButtonFunction.RejectCall => 0x11,
            NuraButtonFunction.PlayPauseAndAnswerCall => 0x12,
            NuraButtonFunction.VoiceAssistant => 0x13,
            NuraButtonFunction.Mute => 0x14,
            NuraButtonFunction.TogglePassthroughAndPause => 0x15,
            NuraButtonFunction.KickItUp => 0x16,
            NuraButtonFunction.KickItDown => 0x17,
            NuraButtonFunction.ToggleSpatial => 0x18,
            NuraButtonFunction.ToggleGamingMode => 0x19,
            NuraButtonFunction.Unknown => throw new ArgumentOutOfRangeException(nameof(function), function, "Unknown button function cannot be serialized."),
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "Unsupported button function.")
        };
}
