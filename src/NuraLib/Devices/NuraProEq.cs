namespace NuraLib.Devices;

public sealed record class NuraProEq {
    public bool Enabled { get; init; }

    public IReadOnlyList<float> Bands { get; init; } = Array.Empty<float>();
}
