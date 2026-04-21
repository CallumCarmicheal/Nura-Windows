using System.Windows.Controls;

using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Controls;

public partial class ExpandedDevicePageControl : UserControl
{
    private DeviceProfilesPreviewController? _devicePreviewController;

    public ExpandedDevicePageControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e) {
        _devicePreviewController ??= new DeviceProfilesPreviewController(
            DeviceListBox,
            () => (DataContext as MainViewModel)?.UseBitmapProfileRenderer ?? false);
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e) {
        _devicePreviewController?.Dispose();
        _devicePreviewController = null;
    }
}
