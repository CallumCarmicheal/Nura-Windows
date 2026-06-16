namespace NuraLib.Devices;

/// <summary>
/// Represents the raw hearing-profile visualisation data used by the official Nura renderers.
/// </summary>
public sealed record class NuraProfileVisualisationData {
    /// <summary>
    /// Gets a shared empty visualisation value.
    /// </summary>
    public static NuraProfileVisualisationData Empty { get; } = new() {
        Valid = false,
        Colour = 0.0,
        LeftData = [],
        RightData = []
    };

    /// <summary>
    /// Gets a value indicating whether the source visualisation payload was valid.
    /// </summary>
    public bool Valid { get; init; }

    /// <summary>
    /// Gets the colour scalar used to select the profile gradient.
    /// </summary>
    public double Colour { get; init; }

    /// <summary>
    /// Gets the raw left-ear profile values.
    /// </summary>
    public IReadOnlyList<double> LeftData { get; init; } = [];

    /// <summary>
    /// Gets the raw right-ear profile values.
    /// </summary>
    public IReadOnlyList<double> RightData { get; init; } = [];
}
