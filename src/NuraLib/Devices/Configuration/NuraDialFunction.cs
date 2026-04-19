namespace NuraLib.Devices;

/// <summary>
/// Represents a supported NuraLoop dial function.
/// </summary>
public enum NuraDialFunction {
    /// <summary>
    /// No function is assigned.
    /// </summary>
    None,

    /// <summary>
    /// The dial controls immersion or Kick-It.
    /// </summary>
    Kickit,

    /// <summary>
    /// The dial controls ANC.
    /// </summary>
    Anc,

    /// <summary>
    /// The dial controls volume.
    /// </summary>
    Volume
}
