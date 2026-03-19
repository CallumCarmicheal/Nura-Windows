namespace desktop_app;

internal static class ArgumentReader {
    public static bool HasFlag(string[] args, string flag)
        => args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));

    public static string? OptionalValue(string[] args, string name) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) {
                return args[i + 1];
            }
        }

        return null;
    }

    public static string RequiredValue(string[] args, string name) {
        for (var i = 0; i < args.Length - 1; i++) {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) {
                return args[i + 1];
            }
        }

        throw new InvalidOperationException($"missing required argument {name}");
    }
}
