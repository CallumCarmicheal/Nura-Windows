using System.Windows;
using System.Windows.Controls;

namespace NuraDesktop.Controls;

public partial class ShellHeaderControl : UserControl {
    private bool _isExitPromptOpen;

    public ShellHeaderControl() {
        InitializeComponent();
    }

    private async void ExitButton_OnClick(object sender, RoutedEventArgs e) {
        if (_isExitPromptOpen) {
            return;
        }

        var owner = Window.GetWindow(this);
        var confirmationWindow = new ExitConfirmationWindow {
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };

        if (owner is not null) {
            confirmationWindow.Owner = owner;
        }

        _isExitPromptOpen = true;

        try {
            if (confirmationWindow.ShowDialog() != true) {
                return;
            }

            if (Application.Current is App app) {
                await app.ShutdownCleanlyAsync();
                return;
            }

            Application.Current.Shutdown();
        } finally {
            _isExitPromptOpen = false;
        }
    }
}
