namespace NuraLib.Devices;

/// <summary>
/// Identifies the normalized action assigned to a headset button gesture.
/// </summary>
public enum NuraButtonFunction {
    None = 0,
    PlayPauseAndCall,
    PlayPauseOnly,
    ToggleKickIt,
    HoldForPassthroughOnOneSide,
    HoldForPassthroughOnBothSides,
    TogglePassthroughOnOneSide,
    TogglePassthroughOnBothSides,
    ToggleSocial,
    ToggleGenericModeEnabled,
    PreviousTrack,
    NextTrack,
    VolumeUp,
    VolumeDown,
    ToggleAnc,
    CycleAncPassthrough,
    SpeakBatteryLevel,
    RejectCall,
    PlayPauseAndAnswerCall,
    VoiceAssistant,
    TogglePassthroughAndPause,
    KickItUp,
    KickItDown,
    Unknown
}
