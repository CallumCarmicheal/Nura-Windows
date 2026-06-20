using System.Windows;
using System.Windows.Input;

namespace NuraDesktop;

public partial class PrereleaseWarningWindow : Window {
    public PrereleaseWarningWindow() {
        InitializeComponent();
    }

    public bool DoNotShowAgain => DoNotShowAgainCheckBox.IsChecked == true;

    private void ContinueButton_OnClick(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState == MouseButtonState.Pressed) {
            DragMove();
        }
    }
}
