namespace NuraLib.Devices;

[Flags]
public enum NuraDeviceRefreshScope {
    None = 0,
    State = 1 << 0,
    Configuration = 1 << 1,
    Profiles = 1 << 2,
    All = State | Configuration | Profiles
}
