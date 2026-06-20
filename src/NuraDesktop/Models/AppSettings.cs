namespace NuraPopupWpf.Models;

public sealed class AppSettings {
    public bool AutoSetupDevices { get; set; } = true;

    public AppPreferences Preferences { get; set; } = new();
}

public sealed class AppPreferences {
    public WindowAnchorMode AnchorMode { get; set; } = WindowAnchorMode.AnchorEdge;

    public WindowAnchorEdge AnchorEdge { get; set; } = WindowAnchorEdge.Center;

    public RememberExpandType RememberExpandType { get; set; } = RememberExpandType.BasedOnPosition;

    public bool DoNotShowPreReleaseWarning { get; set; }

    public double? LastLeft { get; set; }

    public double? LastTop { get; set; }
}
