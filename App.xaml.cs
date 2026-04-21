using System.Windows;

namespace NuraPopupWpf;

public partial class App : Application {

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // Initialize the NuraLib library
    }

    protected override void OnExit(ExitEventArgs e) {
        base.OnExit(e);

        // Handle cleanup and closure of the NuraLib library.
    }
}
