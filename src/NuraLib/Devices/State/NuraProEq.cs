namespace NuraLib.Devices;

/// <summary>
/// Represents the last known ProEQ state for supported devices.
/// </summary>
public sealed record class NuraProEq {
    /// <summary>
    /// Gets a value indicating whether ProEQ is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the band values that make up the current ProEQ curve.
    /// </summary>
    public IReadOnlyList<float> Bands { get; init; } = Array.Empty<float>();
}
