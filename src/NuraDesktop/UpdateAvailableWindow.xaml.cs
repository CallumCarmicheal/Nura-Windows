using System.Windows;
using System.Windows.Input;

using NuraDesktop.Services;

namespace NuraDesktop;

public partial class UpdateAvailableWindow : Window {
    private readonly DesktopUpdateService _updates;
    private bool _isInstalling;

    public UpdateAvailableWindow(DesktopUpdateService updates) {
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
        DataContext = updates;
        InitializeComponent();
    }

    private void LaterButton_OnClick(object sender, RoutedEventArgs e) {
        PersistSkipIfRequested();
        DialogResult = false;
    }

    private void ViewReleaseButton_OnClick(object sender, RoutedEventArgs e) {
        _updates.OpenAvailableRelease();
    }

    private async void UpdateNowButton_OnClick(object sender, RoutedEventArgs e) {
        _isInstalling = true;
        try {
            await _updates.DownloadAndInstallAsync();
        } finally {
            _isInstalling = false;
        }
    }

    private void Window_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        if (!_isInstalling) {
            PersistSkipIfRequested();
        }
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape && !_updates.IsBusy) {
            PersistSkipIfRequested();
            DialogResult = false;
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState == MouseButtonState.Pressed) {
            DragMove();
        }
    }

    private void PersistSkipIfRequested() {
        if (SkipVersionCheckBox.IsChecked == true) {
            _updates.SkipAvailableUpdate();
        }
    }
}
