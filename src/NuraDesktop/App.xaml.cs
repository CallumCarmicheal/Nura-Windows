using NuraPopupWpf.Bootstrap;
using NuraPopupWpf.Services;

using System.Windows;
using System.Windows.Threading;

namespace NuraPopupWpf;

public partial class App : Application {
    private PopupAppContext? _context;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        Thread.CurrentThread.Name = "NuraPopupWpf UI";

        try {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

            var mode = PopupBootstrapperFactory.ParseMode(e.Args);
            var storagePaths = PopupAppStoragePaths.Create(mode);

            var bootstrapper = PopupBootstrapperFactory.Create(e.Args);
            _context = await bootstrapper.BootstrapAsync(e.Args);

            var window = new MainWindow(_context.RootViewModel);
            MainWindow = window;

            window.Show();

            await Dispatcher.InvokeAsync(
                () => ShowPreReleaseWarningIfNeeded(storagePaths, window),
                DispatcherPriority.ApplicationIdle);

            window.Show();

            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
        } catch (Exception ex) {
            MessageBox.Show(
                $"Startup failed.\n\n{ex.Message}",
                "Nura Popup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void ShowPreReleaseWarningIfNeeded(
        PopupAppStoragePaths storagePaths,
        Window owner) {
        var settingsStore = new AppSettingStore(storagePaths.AppSettingsPath);
        var settings = settingsStore.Load();

        if (settings.Preferences.DoNotShowPreReleaseWarning) {
            return;
        }

        var warningWindow = new PrereleaseWarningWindow {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        warningWindow.ShowDialog();
        warningWindow.Close();

        if (!warningWindow.DoNotShowAgain) {
            return;
        }

        settings.Preferences.DoNotShowPreReleaseWarning = true;
        settingsStore.Save(settings);
    }

    protected override async void OnExit(ExitEventArgs e) {
        if (_context is not null) {
            await _context.DisposeAsync();
            _context = null;
        }

        base.OnExit(e);
    }
}
