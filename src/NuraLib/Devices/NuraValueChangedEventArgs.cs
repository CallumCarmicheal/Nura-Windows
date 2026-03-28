namespace NuraLib.Devices;

public sealed class NuraValueChangedEventArgs<T> : EventArgs {
    public NuraValueChangedEventArgs(T? previous, T? current) {
        Previous = previous;
        Current = current;
    }

    public T? Previous { get; }

    public T? Current { get; }
}
