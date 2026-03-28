namespace NuraLib.Devices;

public sealed record class NuraAncState {
    public NuraAncMode Mode { get; init; } = NuraAncMode.Unknown;

    public byte? PrimaryRaw { get; init; }

    public byte? SecondaryRaw { get; init; }
}
