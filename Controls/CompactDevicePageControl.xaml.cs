using System.Windows.Controls;

using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Controls;

public partial class CompactDevicePageControl : UserControl
{
    private DeviceProfilesPreviewController? _devicePreviewController;

    public CompactDevicePageControl()
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

    private void ProfileSelectorListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (ProfileSelectorButton is not null) {
            ProfileSelectorButton.IsChecked = false;
        }
    }

    private void MoreDevicesPopup_OnClosed(object sender, System.EventArgs e) {
        if (MoreDevicesButton is not null) {
            MoreDevicesButton.IsChecked = false;
        }
    }
}
