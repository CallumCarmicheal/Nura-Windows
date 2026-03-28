namespace NuraLib.Devices;

public sealed record class NuraButtonConfiguration {
    public IReadOnlyDictionary<string, string> Bindings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
