namespace NuraLib.Protocol;

public enum HeadsetIndicationIdentifier : byte {
    GenericModeEnabledChanged = 0,
    CableChanged = 1,
    AudioPromptFinished = 2,
    CurrentProfileChanged = 3,
    KickitEnabledChanged = 4,
    TouchButtonPressed = 5,
    AncParametersChanged = 6,
    AncLevelChanged = 7,
    TouchDial = 8,
    KickitLevelChanged = 9
}
