namespace NuraPopupWpf.Bootstrap;

public static class PopupBootstrapperFactory {
    public static PopupAppBootstrapMode ParseMode(string[] args) {
        foreach (var argument in args) {
            if (argument.StartsWith("--bootstrap=", StringComparison.OrdinalIgnoreCase)) {
                var value = argument[(argument.IndexOf('=') + 1)..].Trim();
                if (string.Equals(value, "demo", StringComparison.OrdinalIgnoreCase)) {
                    return PopupAppBootstrapMode.Demo;
                }
            }
        }

        return PopupAppBootstrapMode.Live;
    }

    public static IPopupAppBootstrapper Create(string[] args) {
        return ParseMode(args) == PopupAppBootstrapMode.Demo
            ? new DemoSeedBootstrapper()
            : new LiveSdkBootstrapper();
    }
}
