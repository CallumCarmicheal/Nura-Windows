namespace NuraLib.Devices;

/// <summary>
/// Describes whether personalised Nura processing is active or bypassed.
/// </summary>
public enum NuraPersonalisationMode {
    /// <summary>
    /// Personalised processing is disabled and the device is operating in neutral mode.
    /// </summary>
    Neutral = 0,

    /// <summary>
    /// Personalised processing is enabled.
    /// </summary>
    Personalised = 1
}
