namespace NuraLib.Devices;

/// <summary>
/// Represents a normalized left and right dial configuration for supported devices such as NuraLoop.
/// </summary>
public sealed record class NuraDialConfiguration {
    /// <summary>
    /// Gets the function assigned to the left dial.
    /// </summary>
    public NuraDialFunction Left { get; init; }

    /// <summary>
    /// Gets the function assigned to the right dial.
    /// </summary>
    public NuraDialFunction Right { get; init; }

    /// <summary>
    /// Gets the function assigned to the specified dial side.
    /// </summary>
    /// <param name="side">The dial side to inspect.</param>
    public NuraDialFunction GetBinding(NuraDialSide side) {
        return side switch {
            NuraDialSide.Left => Left,
            NuraDialSide.Right => Right,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unsupported dial side.")
        };
    }

    /// <summary>
    /// Creates a copy of this configuration with a single dial side changed.
    /// </summary>
    /// <param name="side">The dial side to update.</param>
    /// <param name="function">The function to assign.</param>
    public NuraDialConfiguration WithBinding(NuraDialSide side, NuraDialFunction function) {
        return side switch {
            NuraDialSide.Left => this with { Left = function },
            NuraDialSide.Right => this with { Right = function },
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unsupported dial side.")
        };
    }
}
