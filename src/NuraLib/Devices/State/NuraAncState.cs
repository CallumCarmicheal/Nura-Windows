namespace NuraLib.Devices;

/// <summary>
/// Describes the current ANC and passthrough state reported or requested by the library.
/// </summary>
public sealed record class NuraAncState {
    /// <summary>
    /// Gets a value indicating whether ANC is enabled.
    /// </summary>
    public bool AncEnabled { get; init; }

    /// <summary>
    /// Gets a value indicating whether passthrough is enabled.
    /// </summary>
    public bool PassthroughEnabled { get; init; }

    /// <summary>
    /// Gets the normalized ANC mode derived from the current boolean state.
    /// </summary>
    public NuraAncMode Mode => PassthroughEnabled
        ? NuraAncMode.Passthrough
        : AncEnabled
            ? NuraAncMode.Anc
            : NuraAncMode.Off;
}
