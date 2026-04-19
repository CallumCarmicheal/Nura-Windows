namespace NuraLib.Devices;

/// <summary>
/// Describes interaction and control-surface capabilities supported by a device family and firmware combination.
/// </summary>
[Flags]
public enum NuraInteractionCapabilities {
    /// <summary>
    /// No interaction capabilities are known to be supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// Touch-button configuration is supported.
    /// </summary>
    TouchButtons = 1 << 0,

    /// <summary>
    /// Dial configuration is supported.
    /// </summary>
    Dial = 1 << 1,

    /// <summary>
    /// Head detection support is available.
    /// </summary>
    HeadDetection = 1 << 2,

    /// <summary>
    /// Manual head detection support is available.
    /// </summary>
    ManualHeadDetection = 1 << 3,

    /// <summary>
    /// The device supports double-tap gesture slots.
    /// </summary>
    DoubleTap = 1 << 4,

    /// <summary>
    /// The device supports triple-tap gesture slots.
    /// </summary>
    TripleTap = 1 << 5,

    /// <summary>
    /// Voice prompt gain control is supported.
    /// </summary>
    VoicePromptGain = 1 << 6,

    /// <summary>
    /// Play/pause and answer-call button functions are supported.
    /// </summary>
    ButtonPlayPauseAnswer = 1 << 7,

    /// <summary>
    /// Voice assistant button functions are supported.
    /// </summary>
    ButtonVoiceAssistant = 1 << 8,

    /// <summary>
    /// Volume up/down button functions are supported.
    /// </summary>
    ButtonVolumeUpDown = 1 << 9,

    /// <summary>
    /// Previous/next track button functions are supported.
    /// </summary>
    ButtonPrevNextTrack = 1 << 10,

    /// <summary>
    /// Toggle-social button functions are supported.
    /// </summary>
    ButtonToggleSocial = 1 << 11,

    /// <summary>
    /// Kick-It up/down button functions are supported.
    /// </summary>
    ButtonKickItUpDown = 1 << 12
}
