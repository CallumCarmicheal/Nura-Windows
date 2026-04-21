using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using NuraPopupWpf.Models;

namespace NuraPopupWpf.Controls;

internal sealed class DeviceProfilesPreviewController : IDisposable {
    private const double PopupMinWidth = 180.0;
    private const double PreviewArrowWidth = 18.0;
    private const double PreviewArrowHeight = 11.0;
    private const double PreviewArrowEdgePadding = 18.0;
    private const double PreviewVerticalGap = 10.0;
    private const double PreviewCardPadding = 14.0;

    private readonly ListBox _listBox;
    private readonly Func<bool> _useBitmapRendererAccessor;

    private Popup? _popup;
    private Grid? _popupRoot;
    private Path? _arrow;
    private TextBlock? _titleText;
    private StackPanel? _profilesPanel;
    private ListBoxItem? _hoveredItem;
    private DeviceModel? _hoveredDevice;

    public DeviceProfilesPreviewController(ListBox listBox, Func<bool> useBitmapRendererAccessor) {
        _listBox = listBox;
        _useBitmapRendererAccessor = useBitmapRendererAccessor;

        _listBox.PreviewMouseMove += OnListBoxPreviewMouseMove;
        _listBox.MouseLeave += OnListBoxMouseLeave;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
    }

    public void Dispose() {
        _listBox.PreviewMouseMove -= OnListBoxPreviewMouseMove;
        _listBox.MouseLeave -= OnListBoxMouseLeave;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        ClosePreview();
    }

    private void OnListBoxPreviewMouseMove(object sender, MouseEventArgs e) {
        var hoveredItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (hoveredItem?.DataContext is not DeviceModel device) {
            ClosePreview();
            return;
        }

        _hoveredItem = hoveredItem;
        _hoveredDevice = device;

        if (!IsPreviewModifierActive()) {
            ClosePreview();
            return;
        }

        ShowPreview(device, hoveredItem, e.GetPosition(_listBox).X);
    }

    private void OnListBoxMouseLeave(object sender, MouseEventArgs e) {
        _hoveredItem = null;
        _hoveredDevice = null;
        ClosePreview();
    }

    private void OnPreProcessInput(object? sender, PreProcessInputEventArgs e) {
        if (!IsPreviewModifierActive()) {
            ClosePreview();
            return;
        }

        if (_hoveredItem is null || _hoveredDevice is null || !_hoveredItem.IsMouseOver) {
            return;
        }

        ShowPreview(_hoveredDevice, _hoveredItem, Mouse.GetPosition(_listBox).X);
    }

    private void ShowPreview(DeviceModel device, ListBoxItem item, double cursorX) {
        EnsurePopup();
        if (_popup is null || _popupRoot is null || _arrow is null || _titleText is null || _profilesPanel is null) {
            return;
        }

        _titleText.Text = device.Name;
        RebuildProfiles(device);
        UpdatePopupPosition(item, cursorX);
        _popup.IsOpen = true;
    }

    private void RebuildProfiles(DeviceModel device) {
        if (_profilesPanel is null || _popupRoot is null) {
            return;
        }

        _profilesPanel.Children.Clear();

        foreach (var profile in device.Profiles) {
            var profileColumn = new StackPanel {
                Width = 64,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var visualHost = new Grid {
                Width = 60,
                Height = 60,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var halo = new Ellipse {
                Width = 60,
                Height = 60,
                Fill = new RadialGradientBrush {
                    Center = new Point(0.5, 0.5),
                    GradientOrigin = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                    GradientStops = new GradientStopCollection {
                        new GradientStop((Color)ColorConverter.ConvertFromString("#14FF57B9"), 0.0),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#10FF57B9"), 0.42),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#08FF57B9"), 0.66),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#02FF57B9"), 0.86),
                        new GradientStop((Color)ColorConverter.ConvertFromString("#00FF57B9"), 1.0)
                    }
                }
            };
            visualHost.Children.Add(halo);

            visualHost.Children.Add(new Ellipse {
                Width = 52,
                Height = 52,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")),
                StrokeThickness = 1
            });

            visualHost.Children.Add(new ProfileVisualControl {
                Width = 48,
                Height = 48,
                FromProfile = profile,
                ToProfile = profile,
                ProfileBlendProgress = 1.0,
                ModeProgress = 1.0,
                ImmersionValue = 1,
                RenderShadow = true,
                UseBitmapRenderer = _useBitmapRendererAccessor(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            profileColumn.Children.Add(visualHost);
            profileColumn.Children.Add(new TextBlock {
                Text = profile.Name,
                Margin = new Thickness(0, 8, 0, 0),
                Foreground = Brushes.White,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            _profilesPanel.Children.Add(profileColumn);
        }

        if (_profilesPanel.Children.Count > 0 && _profilesPanel.Children[^1] is FrameworkElement lastColumn) {
            lastColumn.Margin = new Thickness(0);
        }

        var popupWidth = Math.Max(
            PopupMinWidth,
            (device.Profiles.Count * 64.0) + ((Math.Max(0, device.Profiles.Count - 1)) * 10.0) + (PreviewCardPadding * 2.0));

        _popupRoot.Width = popupWidth;
    }

    private void UpdatePopupPosition(ListBoxItem item, double cursorX) {
        if (_popup is null || _popupRoot is null || _arrow is null) {
            return;
        }

        var itemBounds = item.TransformToAncestor(_listBox)
            .TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));

        var popupWidth = _popupRoot.Width > 0 ? _popupRoot.Width : PopupMinWidth;
        var maxLeft = Math.Max(0, _listBox.ActualWidth - popupWidth);
        var popupLeft = Math.Clamp(cursorX - (popupWidth * 0.5), 0, maxLeft);
        var arrowLeft = Math.Clamp(
            cursorX - popupLeft - (PreviewArrowWidth * 0.5),
            PreviewArrowEdgePadding,
            popupWidth - PreviewArrowWidth - PreviewArrowEdgePadding);

        _popup.HorizontalOffset = popupLeft;
        _popup.VerticalOffset = itemBounds.Bottom + PreviewVerticalGap;
        _arrow.Margin = new Thickness(arrowLeft, 3, 0, 0);
    }

    private void ClosePreview() {
        if (_popup is not null) {
            _popup.IsOpen = false;
        }
    }

    private void EnsurePopup() {
        if (_popup is not null) {
            return;
        }

        _popup = new Popup {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = _listBox,
            StaysOpen = true,
            IsOpen = false,
            PopupAnimation = PopupAnimation.Fade
        };

        _popupRoot = new Grid {
            Width = PopupMinWidth,
            IsHitTestVisible = false
        };

        var card = new Border {
            Margin = new Thickness(0, 8, 0, 0),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F6292929")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")),
            BorderThickness = new Thickness(1.1),
            Padding = new Thickness(PreviewCardPadding)
        };

        card.Effect = new System.Windows.Media.Effects.DropShadowEffect {
            BlurRadius = 26,
            ShadowDepth = 0,
            Color = Colors.Black,
            Opacity = 0.45
        };

        var stack = new StackPanel();
        _titleText = new TextBlock {
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78FFFFFF")),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        stack.Children.Add(_titleText);

        _profilesPanel = new StackPanel {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(_profilesPanel);

        card.Child = stack;

        _arrow = new Path {
            Width = PreviewArrowWidth,
            Height = PreviewArrowHeight,
            Data = Geometry.Parse($"M 0,{PreviewArrowHeight} L {PreviewArrowWidth * 0.5},0 L {PreviewArrowWidth},{PreviewArrowHeight} Z"),
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D2D2D")),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#52FFFFFF")),
            StrokeThickness = 1.15,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((PopupMinWidth - PreviewArrowWidth) * 0.5, 3, 0, 0)
        };

        _popupRoot.Children.Add(_arrow);
        _popupRoot.Children.Add(card);
        _popup.Child = _popupRoot;
    }

    private static bool IsPreviewModifierActive() {
        var modifiers = Keyboard.Modifiers;
        return modifiers.HasFlag(ModifierKeys.Shift) || modifiers.HasFlag(ModifierKeys.Control);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject {
        var parent = current;

        while (parent is not null) {
            if (parent is T match) {
                return match;
            }

            parent = GetParent(parent);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current) {
        if (current is Visual || current is System.Windows.Media.Media3D.Visual3D) {
            return VisualTreeHelper.GetParent(current);
        }

        if (current is FrameworkContentElement frameworkContentElement) {
            return frameworkContentElement.Parent
                   ?? ContentOperations.GetParent(frameworkContentElement);
        }

        if (current is ContentElement contentElement) {
            return ContentOperations.GetParent(contentElement);
        }

        return LogicalTreeHelper.GetParent(current);
    }
}
