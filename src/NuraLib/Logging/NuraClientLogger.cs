namespace NuraLib.Logging;

internal sealed class NuraClientLogger {
    private readonly Action<NuraLogEventArgs> _emit;

    public NuraClientLogger(Action<NuraLogEventArgs> emit) {
        _emit = emit ?? throw new ArgumentNullException(nameof(emit));
    }

    public void Trace(string source, string message) => Log(NuraLogLevel.Trace, source, message);

    public void Debug(string source, string message) => Log(NuraLogLevel.Debug, source, message);

    public void Information(string source, string message) => Log(NuraLogLevel.Information, source, message);

    public void Warning(string source, string message) => Log(NuraLogLevel.Warning, source, message);

    public void Error(string source, string message, Exception? exception = null) => Log(NuraLogLevel.Error, source, message, exception);

    public void Log(NuraLogLevel level, string source, string message, Exception? exception = null) {
        _emit(new NuraLogEventArgs(level, source, message, DateTimeOffset.UtcNow, exception));
    }
}
