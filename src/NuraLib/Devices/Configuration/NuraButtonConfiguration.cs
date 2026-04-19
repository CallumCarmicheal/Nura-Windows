namespace NuraLib.Devices;

/// <summary>
/// Represents a normalized per-side touch-button configuration.
/// </summary>
public sealed record class NuraButtonConfiguration {
    /// <summary>
    /// Gets the action assigned to the left single-tap gesture.
    /// </summary>
    public NuraButtonFunction? LeftSingleTap { get; init; }

    /// <summary>
    /// Gets the action assigned to the right single-tap gesture.
    /// </summary>
    public NuraButtonFunction? RightSingleTap { get; init; }

    /// <summary>
    /// Gets the action assigned to the left double-tap gesture.
    /// </summary>
    public NuraButtonFunction? LeftDoubleTap { get; init; }

    /// <summary>
    /// Gets the action assigned to the right double-tap gesture.
    /// </summary>
    public NuraButtonFunction? RightDoubleTap { get; init; }

    /// <summary>
    /// Gets the action assigned to the left triple-tap gesture.
    /// </summary>
    public NuraButtonFunction? LeftTripleTap { get; init; }

    /// <summary>
    /// Gets the action assigned to the right triple-tap gesture.
    /// </summary>
    public NuraButtonFunction? RightTripleTap { get; init; }

    /// <summary>
    /// Gets the action assigned to the left tap-and-hold gesture.
    /// </summary>
    public NuraButtonFunction? LeftTapAndHold { get; init; }

    /// <summary>
    /// Gets the action assigned to the right tap-and-hold gesture.
    /// </summary>
    public NuraButtonFunction? RightTapAndHold { get; init; }

    /// <summary>
    /// Returns the configured action for the requested side and gesture.
    /// </summary>
    /// <param name="side">The side to query.</param>
    /// <param name="gesture">The gesture to query.</param>
    public NuraButtonFunction? GetBinding(NuraButtonSide side, NuraButtonGesture gesture) {
        return (side, gesture) switch {
            (NuraButtonSide.Left, NuraButtonGesture.SingleTap) => LeftSingleTap,
            (NuraButtonSide.Right, NuraButtonGesture.SingleTap) => RightSingleTap,
            (NuraButtonSide.Left, NuraButtonGesture.DoubleTap) => LeftDoubleTap,
            (NuraButtonSide.Right, NuraButtonGesture.DoubleTap) => RightDoubleTap,
            (NuraButtonSide.Left, NuraButtonGesture.TripleTap) => LeftTripleTap,
            (NuraButtonSide.Right, NuraButtonGesture.TripleTap) => RightTripleTap,
            (NuraButtonSide.Left, NuraButtonGesture.TapAndHold) => LeftTapAndHold,
            (NuraButtonSide.Right, NuraButtonGesture.TapAndHold) => RightTapAndHold,
            _ => null
        };
    }

    /// <summary>
    /// Returns a copy of the configuration with one binding replaced.
    /// </summary>
    /// <param name="side">The side to modify.</param>
    /// <param name="gesture">The gesture to modify.</param>
    /// <param name="function">The action to assign, or <see langword="null"/> to clear the slot.</param>
    public NuraButtonConfiguration WithBinding(
        NuraButtonSide side,
        NuraButtonGesture gesture,
        NuraButtonFunction? function) {
        return (side, gesture) switch {
            (NuraButtonSide.Left, NuraButtonGesture.SingleTap) => this with { LeftSingleTap = function },
            (NuraButtonSide.Right, NuraButtonGesture.SingleTap) => this with { RightSingleTap = function },
            (NuraButtonSide.Left, NuraButtonGesture.DoubleTap) => this with { LeftDoubleTap = function },
            (NuraButtonSide.Right, NuraButtonGesture.DoubleTap) => this with { RightDoubleTap = function },
            (NuraButtonSide.Left, NuraButtonGesture.TripleTap) => this with { LeftTripleTap = function },
            (NuraButtonSide.Right, NuraButtonGesture.TripleTap) => this with { RightTripleTap = function },
            (NuraButtonSide.Left, NuraButtonGesture.TapAndHold) => this with { LeftTapAndHold = function },
            (NuraButtonSide.Right, NuraButtonGesture.TapAndHold) => this with { RightTapAndHold = function },
            _ => this
        };
    }
}
