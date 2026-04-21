namespace NuraPopupWpf.Models;

public sealed class WindowAnchorOption {
    public WindowAnchorOption(WindowAnchorMode mode, string label, string subtitle) {
        Mode = mode;
        Label = label;
        Subtitle = subtitle;
    }

    public WindowAnchorMode Mode { get; }

    public string Label { get; }

    public string Subtitle { get; }

    public override string ToString() => Label;
}
