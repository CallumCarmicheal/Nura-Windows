namespace NuraLib.Logging;

internal sealed class NuraClientLogger {
    private readonly Action<NuraLogEventArgs> _emit;
    private readonly Func<NuraLogLevel> _minimumLevel;

    public NuraClientLogger(Action<NuraLogEventArgs> emit, Func<NuraLogLevel>? minimumLevel = null) {
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
        _minimumLevel = minimumLevel ?? (() => NuraLogLevel.Trace);
    }

    public bool IsEnabled(NuraLogLevel level) => level >= _minimumLevel();

    public void Trace(string source, string message) => Log(NuraLogLevel.Trace, source, message);

    public void Debug(string source, string message) => Log(NuraLogLevel.Debug, source, message);

    public void Information(string source, string message) => Log(NuraLogLevel.Information, source, message);

    public void Warning(string source, string message) => Log(NuraLogLevel.Warning, source, message);

    public void Error(string source, string message, Exception? exception = null) => Log(NuraLogLevel.Error, source, message, exception);

    public void Log(NuraLogLevel level, string source, string message, Exception? exception = null) {
        if (!IsEnabled(level)) {
            return;
        }

        _emit(new NuraLogEventArgs(level, source, message, DateTimeOffset.UtcNow, exception));
    }
}
