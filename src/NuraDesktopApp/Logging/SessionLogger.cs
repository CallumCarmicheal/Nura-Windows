namespace desktop_app.Logging;

internal sealed class SessionLogger : IDisposable {
    private readonly StreamWriter _writer;
    private bool _disposed;

    private SessionLogger(string logPath, StreamWriter writer) {
        LogPath = logPath;
        _writer = writer;
    }

    public string LogPath { get; }

    public static SessionLogger CreateDefault() {
        var root = ResolveLogRoot();
        Directory.CreateDirectory(root);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        var logPath = Path.Combine(root, $"session_{timestamp}.log");
        var stream = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream) {
            AutoFlush = true
        };

        var logger = new SessionLogger(logPath, writer);
        logger.WriteLine($"session.started_utc={DateTimeOffset.UtcNow:O}");
        return logger;
    }

    public void WriteLine(string? message = null) {
        ThrowIfDisposed();
        var text = message ?? string.Empty;
        _writer.WriteLine(text);
        WriteConsoleLine(text);
    }

    public void Error(string message) {
        ThrowIfDisposed();
        _writer.WriteLine(message);
        WriteConsoleLine(message, forceColor: ConsoleColor.Red, useErrorStream: true);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _writer.Dispose();
        _disposed = true;
    }

    private static string ResolveLogRoot() {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "logs"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "logs"),
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "logs")
        };

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private void ThrowIfDisposed() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(SessionLogger));
        }
    }

    private static void WriteConsoleLine(string text, ConsoleColor? forceColor = null, bool useErrorStream = false) {
        var writer = useErrorStream ? Console.Error : Console.Out;
        if (Console.IsOutputRedirected && !useErrorStream) {
            writer.WriteLine(text);
            return;
        }

        if (Console.IsErrorRedirected && useErrorStream) {
            writer.WriteLine(text);
            return;
        }

        if (string.IsNullOrEmpty(text)) {
            writer.WriteLine();
            return;
        }

        var originalColor = Console.ForegroundColor;
        try {
            if (forceColor is not null) {
                Console.ForegroundColor = forceColor.Value;
                writer.WriteLine(text);
                return;
            }

            if (TryWriteKeyValueLine(writer, text)) {
                return;
            }

            if (TryWriteStatusLine(writer, text)) {
                return;
            }

            Console.ForegroundColor = InferPlainColor(text);
            writer.WriteLine(text);
        } finally {
            Console.ForegroundColor = originalColor;
        }
    }

    private static bool TryWriteKeyValueLine(TextWriter writer, string text) {
        var separator = text.IndexOf('=');
        if (separator <= 0) {
            return false;
        }

        var key = text[..separator];
        var value = separator == text.Length - 1 ? string.Empty : text[(separator + 1)..];

        Console.ForegroundColor = InferKeyColor(key, value);
        writer.Write(key);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        writer.Write('=');
        Console.ForegroundColor = InferValueColor(key, value);
        writer.WriteLine(value);
        return true;
    }

    private static bool TryWriteStatusLine(TextWriter writer, string text) {
        if (text.EndsWith(':') && !text.Contains('=')) {
            Console.ForegroundColor = ConsoleColor.Magenta;
            writer.WriteLine(text);
            return true;
        }

        return false;
    }

    private static ConsoleColor InferPlainColor(string text) {
        return text switch {
            "connecting..." => ConsoleColor.Yellow,
            "connected" => ConsoleColor.Green,
            _ when text.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase) => ConsoleColor.Red,
            _ when text.StartsWith("Usage:", StringComparison.Ordinal) => ConsoleColor.Magenta,
            _ when text.StartsWith("transport=", StringComparison.Ordinal) => ConsoleColor.Cyan,
            _ => ConsoleColor.Gray
        };
    }

    private static ConsoleColor InferKeyColor(string key, string value) {
        if (key.StartsWith("tx.", StringComparison.Ordinal)) {
            return ConsoleColor.Yellow;
        }

        if (key.StartsWith("rx.", StringComparison.Ordinal)) {
            return ConsoleColor.Cyan;
        }

        if (key.StartsWith("post_auth.", StringComparison.Ordinal)) {
            return ConsoleColor.Green;
        }

        if (key.StartsWith("auth.", StringComparison.Ordinal)) {
            return ConsoleColor.Magenta;
        }

        if (key.StartsWith("handshake.", StringComparison.Ordinal)) {
            return value.Equals("True", StringComparison.OrdinalIgnoreCase)
                ? ConsoleColor.Green
                : ConsoleColor.Yellow;
        }

        if (key.StartsWith("device.", StringComparison.Ordinal) ||
            key.StartsWith("session.", StringComparison.Ordinal) ||
            key is "config.path" or "log.path") {
            return ConsoleColor.Blue;
        }

        if (key is "status") {
            return value.Equals("0x00", StringComparison.OrdinalIgnoreCase)
                ? ConsoleColor.Green
                : ConsoleColor.Yellow;
        }

        if (key is "command" or "command_raw" or "vendor" or "length") {
            return ConsoleColor.DarkCyan;
        }

        return ConsoleColor.DarkGray;
    }

    private static ConsoleColor InferValueColor(string key, string value) {
        if (key is "status") {
            return value.Equals("0x00", StringComparison.OrdinalIgnoreCase)
                ? ConsoleColor.Green
                : ConsoleColor.Yellow;
        }

        if (key.StartsWith("handshake.", StringComparison.Ordinal)) {
            return value.Equals("True", StringComparison.OrdinalIgnoreCase)
                ? ConsoleColor.Green
                : ConsoleColor.Yellow;
        }

        if (key.StartsWith("tx.", StringComparison.Ordinal)) {
            return ConsoleColor.White;
        }

        if (key.StartsWith("rx.", StringComparison.Ordinal)) {
            return ConsoleColor.Gray;
        }

        if (key.StartsWith("post_auth.", StringComparison.Ordinal)) {
            return ConsoleColor.White;
        }

        if (key.StartsWith("auth.", StringComparison.Ordinal)) {
            return key.Contains(".token", StringComparison.Ordinal) ||
                   key.EndsWith(".client", StringComparison.Ordinal) ||
                   key.EndsWith(".client_key", StringComparison.Ordinal)
                ? ConsoleColor.DarkYellow
                : ConsoleColor.White;
        }

        if (key.EndsWith(".hex", StringComparison.Ordinal)) {
            return ConsoleColor.Gray;
        }

        if (key is "config.path" or "log.path") {
            return ConsoleColor.DarkYellow;
        }

        return ConsoleColor.Gray;
    }
}
