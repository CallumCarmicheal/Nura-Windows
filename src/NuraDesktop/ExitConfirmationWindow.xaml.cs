using System.Windows;
using System.Windows.Input;

namespace NuraDesktop;

public partial class ExitConfirmationWindow : Window {
    public ExitConfirmationWindow() {
        InitializeComponent();
    }

    private void YesButton_OnClick(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void NoButton_OnClick(object sender, RoutedEventArgs e) {
        DialogResult = false;
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            DialogResult = false;
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState == MouseButtonState.Pressed) {
            DragMove();
        }
    }
}
