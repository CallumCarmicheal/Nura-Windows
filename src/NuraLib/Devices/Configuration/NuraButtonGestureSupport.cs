namespace NuraLib.Devices;

/// <summary>
/// Describes which touch-button gesture slots are supported by a device.
/// </summary>
[Flags]
public enum NuraButtonGestureSupport {
    /// <summary>
    /// No touch-button gestures are supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// Single-tap gesture slots are supported.
    /// </summary>
    SingleTap = 1 << 0,

    /// <summary>
    /// Double-tap gesture slots are supported.
    /// </summary>
    DoubleTap = 1 << 1,

    /// <summary>
    /// Triple-tap gesture slots are supported.
    /// </summary>
    TripleTap = 1 << 2,

    /// <summary>
    /// Tap-and-hold gesture slots are supported.
    /// </summary>
    TapAndHold = 1 << 3
}
