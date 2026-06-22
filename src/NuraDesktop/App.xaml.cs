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
        Thread.CurrentThread.Name = "NuraPopupWpf UI";

        StartupSplashWindow? splash = null;

        try {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

            splash = new StartupSplashWindow();
            splash.Show();
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);

            var mode = PopupBootstrapperFactory.ParseMode(e.Args);
            var storagePaths = PopupAppStoragePaths.Create(mode);

            var bootstrapper = PopupBootstrapperFactory.Create(e.Args);
            splash.SetStatus(mode == PopupAppBootstrapMode.Live
                ? "Starting Bluetooth services..."
                : "Preparing demo devices...");
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            _context = await bootstrapper.BootstrapAsync(e.Args);
            splash.SetStatus("Opening Nura Desktop...");

            var window = new MainWindow(_context.RootViewModel);
            var closedDuringStartup = false;
            window.Closed += (_, _) => closedDuringStartup = true;
            MainWindow = window;

            window.Show();
            splash.Close();
            splash = null;

            await Dispatcher.InvokeAsync(
                () => {
                    if (!closedDuringStartup) {
                        ShowPreReleaseWarningIfNeeded(storagePaths, window);
                    }
                },
                DispatcherPriority.ApplicationIdle);

            if (closedDuringStartup) {
                await ShutdownCleanlyAsync();
                return;
            }

            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;

            await _context.RootViewModel.CheckForUpdatesAsync(surfaceFailures: false);
            if (!closedDuringStartup) {
                ShowUpdateIfAvailable(_context.RootViewModel, window);
            }
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
        Window owner) {
        var settingsStore = new AppSettingStore(storagePaths.AppSettingsPath);
        var settings = settingsStore.Load();

        if (settings.Preferences.DoNotShowPreReleaseWarning) {
            return;
        }

        if (!CanOwnDialog(owner)) {
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
