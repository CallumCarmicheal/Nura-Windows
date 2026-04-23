namespace NuraPopupWpf.Models;

public sealed class WindowPreferences {
    public WindowAnchorMode AnchorMode { get; set; } = WindowAnchorMode.Taskbar;

    public double? LastLeft { get; set; }

    public double? LastTop { get; set; }
}
