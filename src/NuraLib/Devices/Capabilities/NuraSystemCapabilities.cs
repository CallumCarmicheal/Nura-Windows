namespace NuraLib.Devices;

/// <summary>
/// Describes non-audio device and transport capabilities supported by a device family and firmware combination.
/// </summary>
[Flags]
public enum NuraSystemCapabilities {
    /// <summary>
    /// No system capabilities are known to be supported.
    /// </summary>
    None = 0,

    /// <summary>
    /// Device information reads are supported.
    /// </summary>
    DeviceInfo = 1 << 0,

    /// <summary>
    /// Profile reads or selection support is available.
    /// </summary>
    Profiles = 1 << 1,

    /// <summary>
    /// Bulk command support is available.
    /// </summary>
    BulkCommands = 1 << 2,

    /// <summary>
    /// User ID support is available.
    /// </summary>
    UserId = 1 << 3,

    /// <summary>
    /// MSP firmware version reads are supported.
    /// </summary>
    MspFirmwareVersion = 1 << 4,

    /// <summary>
    /// Insertion data V2 reads are supported.
    /// </summary>
    InsertionDataV2 = 1 << 5,

    /// <summary>
    /// Multipoint support is available.
    /// </summary>
    Multipoint = 1 << 6
}
