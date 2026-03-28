using NuraLib.Devices;

namespace NuraLib.Monitoring;

public sealed class NuraDeviceConnectionEventArgs : EventArgs {
    public NuraDeviceConnectionEventArgs(ConnectedNuraDevice device) {
        Device = device;
    }

    public ConnectedNuraDevice Device { get; }
}
