namespace NuraLib.Devices;

public sealed record class NuraDialConfiguration {
    public IReadOnlyDictionary<string, string> Bindings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
