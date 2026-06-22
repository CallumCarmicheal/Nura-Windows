using System.Windows;

namespace NuraDesktop;

public partial class StartupSplashWindow : Window {
    public StartupSplashWindow() {
        InitializeComponent();
    }

    public void SetStatus(string status) {
        StatusTextBlock.Text = status;
    }
}
