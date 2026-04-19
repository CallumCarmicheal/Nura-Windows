using NuraLib.Devices;

namespace NuraLib.Monitoring;

/// <summary>
/// Provides the connected device involved in a monitoring connection event.
/// </summary>
public sealed class NuraDeviceConnectionEventArgs : EventArgs {
    /// <summary>
    /// Creates a new connection-event payload.
    /// </summary>
    /// <param name="device">The connected device associated with the event.</param>
    public NuraDeviceConnectionEventArgs(ConnectedNuraDevice device) {
        Device = device;
    }

    /// <summary>
    /// Gets the connected device associated with the event.
    /// </summary>
    public ConnectedNuraDevice Device { get; }
}
