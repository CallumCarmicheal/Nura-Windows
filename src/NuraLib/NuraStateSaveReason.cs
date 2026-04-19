namespace NuraLib;

[Flags]
/// <summary>
/// Describes why the library is requesting that its state be persisted by the host application.
/// </summary>
public enum NuraStateSaveReason {
    None = 0,
    Configuration = 1 << 0,
    Authentication = 1 << 1,
    DeviceInventory = 1 << 2,
    DeviceKey = 1 << 3,
    Session = 1 << 4,
    Bootstrap = 1 << 5
}
