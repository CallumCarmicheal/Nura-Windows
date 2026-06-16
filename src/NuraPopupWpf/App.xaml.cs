using System.Windows;

using NuraPopupWpf.Bootstrap;

namespace NuraPopupWpf;

public partial class App : Application {
    private PopupAppContext? _context;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        try {
            var bootstrapper = PopupBootstrapperFactory.Create(e.Args);
            _context = await bootstrapper.BootstrapAsync(e.Args);

            var window = new MainWindow(_context.RootViewModel);
            MainWindow = window;
            window.Show();
        } catch (Exception ex) {
            MessageBox.Show(
                $"Startup failed.\n\n{ex.Message}",
                "Nura Popup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e) {
        if (_context is not null) {
            await _context.DisposeAsync();
            _context = null;
        }

        base.OnExit(e);
    }
}
