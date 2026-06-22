using NuraDesktop.Bootstrap;
using NuraDesktop.Services;

using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace NuraDesktop;

public partial class App : Application {
    private PopupAppContext? _context;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        Thread.CurrentThread.Name = "Desktop UI";
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        StartupSplashWindow? splash = null;

        try {
            splash = new StartupSplashWindow {
                ShowInTaskbar = false
            };

            splash.Show();
            await Dispatcher.Yield(DispatcherPriority.Render);

            var mode = PopupBootstrapperFactory.ParseMode(e.Args);
            var storagePaths = PopupAppStoragePaths.Create(mode);

            var bootstrapper = PopupBootstrapperFactory.Create(e.Args);

            splash.SetStatus(mode == PopupAppBootstrapMode.Live
                ? "Starting Bluetooth services..."
                : "Preparing demo devices...");

            await Dispatcher.Yield(DispatcherPriority.Render);

            _context = await bootstrapper.BootstrapAsync(e.Args);

            splash.SetStatus("Checking startup requirements...");
            await Dispatcher.Yield(DispatcherPriority.Render);

            // Show startup gates before MainWindow exists.
            // Either use splash as owner, or make these standalone startup dialogs.
            ShowPreReleaseWarningIfNeeded(storagePaths, splash);

            splash.SetStatus("Checking for updates...");
            await Dispatcher.Yield(DispatcherPriority.Render);

            await _context.RootViewModel.CheckForUpdatesAsync(surfaceFailures: false);
            ShowUpdateIfAvailable(_context.RootViewModel, splash);

            splash.SetStatus("Opening Nura Desktop...");
            await Dispatcher.Yield(DispatcherPriority.Render);

            var window = new MainWindow(_context.RootViewModel);
            MainWindow = window;

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            window.Show();
            window.Activate();

            splash.Close();
            splash = null;
        } catch (Exception ex) {
            splash?.Close();

            MessageBox.Show(
                $"Startup failed.\n\n{ex.Message}",
                "Nura Popup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    public async Task ShutdownCleanlyAsync(int exitCode = 0) {
        if (_isShuttingDown) {
            return;
        }

        _isShuttingDown = true;

        try {
            await DisposeContextAsync();
        } catch (Exception ex) {
            Debug.WriteLine($"Clean shutdown disposal failed: {ex}");
        } finally {
            Shutdown(exitCode);
        }
    }

    private async ValueTask DisposeContextAsync() {
        var context = _context;
        if (context is null) {
            return;
        }

        _context = null;
        await context.DisposeAsync();
    }

    private static void ShowPreReleaseWarningIfNeeded(
        PopupAppStoragePaths storagePaths,
        Window? owner = null) {

        var settingsStore = new AppSettingStore(storagePaths.AppSettingsPath);
        var settings = settingsStore.Load();

        if (settings.Preferences.DoNotShowPreReleaseWarning) {
            return;
        }

        var warningWindow = new PrereleaseWarningWindow {
            WindowStartupLocation = owner is not null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,

            // If there is no owner, let it appear in taskbar so it cannot become
            // an invisible modal blocker.
            ShowInTaskbar = owner is null
        };

        if (owner is not null && CanOwnDialog(owner)) {
            warningWindow.Owner = owner;
            warningWindow.ShowInTaskbar = false;
        }

        warningWindow.ShowDialog();

        if (!warningWindow.DoNotShowAgain) {
            return;
        }

        settings.Preferences.DoNotShowPreReleaseWarning = true;
        settingsStore.Save(settings);
    }

    private static void ShowUpdateIfAvailable(NuraDesktop.ViewModels.MainViewModel viewModel, Window owner) {
        if (!viewModel.ShouldShowStartupUpdatePrompt || !CanOwnDialog(owner)) {
            return;
        }

        var updateWindow = new UpdateAvailableWindow(viewModel.Updates) {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        updateWindow.ShowDialog();
    }

    private static bool CanOwnDialog(Window owner) =>
        owner.IsLoaded &&
        owner.IsVisible &&
        !owner.Dispatcher.HasShutdownStarted &&
        !owner.Dispatcher.HasShutdownFinished;

    protected override async void OnExit(ExitEventArgs e) {
        await DisposeContextAsync();
        base.OnExit(e);
    }
}
