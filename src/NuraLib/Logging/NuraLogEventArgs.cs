namespace NuraLib.Logging;

/// <summary>
/// Provides details for a log message emitted by <c>NuraLib</c>.
/// </summary>
public sealed class NuraLogEventArgs : EventArgs {
    /// <summary>
    /// Creates a new log payload.
    /// </summary>
    /// <param name="level">The severity or verbosity of the message.</param>
    /// <param name="source">The originating class or component name.</param>
    /// <param name="message">The log message text.</param>
    /// <param name="timestampUtc">The UTC time the log entry was created.</param>
    /// <param name="exception">An optional related exception.</param>
    public NuraLogEventArgs(
        NuraLogLevel level,
        string source,
        string message,
        DateTimeOffset timestampUtc,
        Exception? exception = null) {
        Level = level;
        Source = source;
        Message = message;
        TimestampUtc = timestampUtc;
        Exception = exception;
    }

    /// <summary>
    /// Gets the severity or verbosity of the message.
    /// </summary>
    public NuraLogLevel Level { get; }

    /// <summary>
    /// Gets the originating class or component name.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the log message text.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the UTC time the log entry was created.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>
    /// Gets the related exception, when the log entry describes an error.
    /// </summary>
    public Exception? Exception { get; }
}
