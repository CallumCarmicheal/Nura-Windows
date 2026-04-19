namespace NuraLib.Devices;

/// <summary>
/// Describes the supported voice prompt gain presets exposed by newer Nura devices.
/// </summary>
public enum NuraVoicePromptGain {
    /// <summary>
    /// Lower voice prompt volume, mapped by the mobile app to approximately -20 dB.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Default voice prompt volume, mapped by the mobile app to approximately 0 dB.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Higher voice prompt volume, mapped by the mobile app to approximately +20 dB.
    /// </summary>
    High = 2
}
