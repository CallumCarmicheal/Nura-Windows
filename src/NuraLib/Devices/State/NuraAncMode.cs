namespace NuraLib.Devices;

/// <summary>
/// Describes the high-level ANC mode reported or requested by the library.
/// </summary>
public enum NuraAncMode {
    Unknown = 0,
    Off = 1,
    Anc = 2,
    Passthrough = 3
}
