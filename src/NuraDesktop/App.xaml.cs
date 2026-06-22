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

            await _context.RootViewModel.CheckForUpdatesAsync(surfaceFailures: false);
            ShowUpdateIfAvailable(_context.RootViewModel, window);
        } catch (Exception ex) {
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
        if (!viewModel.ShouldShowStartupUpdatePrompt) {
            return;
        }

        var updateWindow = new UpdateAvailableWindow(viewModel.Updates) {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        updateWindow.ShowDialog();
    }

    protected override async void OnExit(ExitEventArgs e) {
        await DisposeContextAsync();
        base.OnExit(e);
    }
}
