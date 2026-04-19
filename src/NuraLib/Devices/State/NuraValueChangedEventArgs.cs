namespace NuraLib.Devices;

/// <summary>
/// Describes a change between two cached device values.
/// </summary>
/// <typeparam name="T">The value type being tracked.</typeparam>
public sealed class NuraValueChangedEventArgs<T> : EventArgs {
    /// <summary>
    /// Creates a new value-change event payload.
    /// </summary>
    /// <param name="previous">The previous cached value.</param>
    /// <param name="current">The new cached value.</param>
    public NuraValueChangedEventArgs(T? previous, T? current) {
        Previous = previous;
        Current = current;
    }

    /// <summary>
    /// Gets the previous cached value.
    /// </summary>
    public T? Previous { get; }

    /// <summary>
    /// Gets the new cached value.
    /// </summary>
    public T? Current { get; }
}
