namespace NuraLib.Devices;

/// <summary>
/// Describes which button-function families are supported by the current device.
/// </summary>
[Flags]
public enum NuraButtonFunctionSupport {
    /// <summary>
    /// No button-function families are known to be supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// Play/pause and answer-call actions are supported.
    /// </summary>
    PlayPauseAnswer = 1 << 0,

    /// <summary>
    /// Voice assistant actions are supported.
    /// </summary>
    VoiceAssistant = 1 << 1,

    /// <summary>
    /// Volume up/down actions are supported.
    /// </summary>
    VolumeUpDown = 1 << 2,

    /// <summary>
    /// Previous/next track actions are supported.
    /// </summary>
    PrevNextTrack = 1 << 3,

    /// <summary>
    /// Toggle-social actions are supported.
    /// </summary>
    ToggleSocial = 1 << 4,

    /// <summary>
    /// Kick-It up/down actions are supported.
    /// </summary>
    KickItUpDown = 1 << 5
}
