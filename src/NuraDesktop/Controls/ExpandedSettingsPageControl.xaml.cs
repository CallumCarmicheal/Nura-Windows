using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Controls;

public partial class ExpandedSettingsPageControl : UserControl {
    private DeviceProfilesPreviewController? _devicePreviewController;
    private object? _pendingAnchorSelection;

    public ExpandedSettingsPageControl() {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        _devicePreviewController ??= new DeviceProfilesPreviewController(
            DeviceListBox,
            () => (DataContext as MainViewModel)?.UseBitmapProfileRenderer ?? false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
        _devicePreviewController?.Dispose();
        _devicePreviewController = null;
    }

    private void MoreDevicesPopup_OnClosed(object sender, System.EventArgs e) {
        if (MoreDevicesButton is not null) {
            MoreDevicesButton.IsChecked = false;
        }
    }

    private void AnchorEdgePopup_OnClosed(object sender, System.EventArgs e) {
        if (AnchorEdgeSelectorButton is not null) {
            AnchorEdgeSelectorButton.IsChecked = false;
        }
    }

    private void AnchorEdgeOptionButton_OnClick(object sender, RoutedEventArgs e) {
        if (AnchorEdgePopup is not null) {
            AnchorEdgePopup.IsOpen = false;
        }
    }

    private void AnchorModeListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (TryGetClickedListBoxItemDataContext(e.OriginalSource as DependencyObject, out var dataContext)) {
            _pendingAnchorSelection = dataContext;
            e.Handled = true;
        }
    }

    private void AnchorModeListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
        if (sender is not ListBox listBox) {
            _pendingAnchorSelection = null;
            return;
        }

        if (TryGetClickedListBoxItemDataContext(e.OriginalSource as DependencyObject, out var dataContext) &&
            Equals(dataContext, _pendingAnchorSelection)) {
            listBox.SelectedItem = dataContext;
            e.Handled = true;
        }

        _pendingAnchorSelection = null;
    }

    private static bool TryGetClickedListBoxItemDataContext(DependencyObject? source, out object? dataContext) {
        var listBoxItem = FindAncestor<ListBoxItem>(source);
        dataContext = listBoxItem?.DataContext;
        return listBoxItem is not null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject {
        while (current is not null) {
            if (current is T match) {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current) {
        if (current is FrameworkElement frameworkElement) {
            return frameworkElement.Parent
                   ?? frameworkElement.TemplatedParent
                   ?? GetVisualParent(current)
                   ?? LogicalTreeHelper.GetParent(current);
        }

        if (current is FrameworkContentElement frameworkContentElement) {
            return frameworkContentElement.Parent
                   ?? frameworkContentElement.TemplatedParent
                   ?? LogicalTreeHelper.GetParent(current);
        }

        return GetVisualParent(current) ?? LogicalTreeHelper.GetParent(current);
    }

    private static DependencyObject? GetVisualParent(DependencyObject current) {
        return current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(current)
            : null;
    }

#region Number Only Text Field
    private static readonly Regex _nonDigitRegex = new("[^0-9]+");

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        e.Handled = _nonDigitRegex.IsMatch(e.Text);
    }

    private void NumberOnly_Pasting(object sender, DataObjectPastingEventArgs e) {
        if (!e.DataObject.GetDataPresent(typeof(string))) {
            e.CancelCommand();
            return;
        }

        var text = (string)e.DataObject.GetData(typeof(string));
        if (_nonDigitRegex.IsMatch(text)) {
            e.CancelCommand();
        }
    }
#endregion
}
