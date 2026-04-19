namespace NuraLib.Devices;

/// <summary>
/// Describes audio-related capabilities supported by a device family and firmware combination.
/// </summary>
[Flags]
public enum NuraAudioCapabilities {
    /// <summary>
    /// No audio capabilities are known to be supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// ANC mode control is supported.
    /// </summary>
    Anc = 1 << 0,

    /// <summary>
    /// Numeric ANC level control is supported.
    /// </summary>
    AncLevel = 1 << 1,

    /// <summary>
    /// A global ANC enable or disable toggle is supported.
    /// </summary>
    GlobalAncToggle = 1 << 2,

    /// <summary>
    /// Immersion or Kick-It state is supported.
    /// </summary>
    Immersion = 1 << 3,

    /// <summary>
    /// Kick-It feature support is available.
    /// </summary>
    KickIt = 1 << 4,

    /// <summary>
    /// Spatial audio support is available.
    /// </summary>
    Spatial = 1 << 5,

    /// <summary>
    /// ProEQ support is available.
    /// </summary>
    ProEq = 1 << 6,

    /// <summary>
    /// Personalised mode support is available.
    /// </summary>
    PersonalisedMode = 1 << 7,

    /// <summary>
    /// EU attenuation support is available.
    /// </summary>
    EuAttenuation = 1 << 8,

    /// <summary>
    /// Separate analog and digital EU attenuation support is available.
    /// </summary>
    AnalogDigitalEuAttenuation = 1 << 9,

    /// <summary>
    /// Visualisation data reads are supported.
    /// </summary>
    VisualisationData = 1 << 10
}
