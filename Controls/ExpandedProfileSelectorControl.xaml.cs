using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

using NuraPopupWpf.Models;
using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Controls;

public partial class ExpandedProfileSelectorControl : UserControl {
    private const double SlashCut = 14.0;
    private const double SeparatorWidth = 18.0;
    private const double PillHeight = 34.0;
    private const double ItemHeight = 34.0;
    private const double PreviewPopupWidth = 232.0;
    private const double PreviewArrowSize = 14.0;
    private const double PreviewVerticalOffset = 12.0;
    private const double PreviewArrowEdgePadding = 18.0;
    private const double PreviewCardPadding = 16.0;
    private const double PreviewVisualHostSize = 196.0;

    private readonly List<Button> _profileButtons = new();
    private readonly List<Border> _iconGlows = new();
    private readonly List<FrameworkElement> _separatorTopHighlights = new();
    private readonly List<FrameworkElement> _separatorBottomHighlights = new();
    private int? _selectionAnimationSourceIndex;
    private int? _selectionAnimationTargetIndex;
    private MainViewModel? _viewModel;
    private bool _isLoaded;
    private PreviewPopupState? _previewPopup;
    private TextBlock? _previewTitle;
    private ProfileVisualControl? _previewVisual;
    private bool _isPreviewAccentActive;
    private Button? _hoveredProfileButton;
    private ProfileModel? _hoveredProfile;
    private DispatcherOperation? _pendingPreviewClose;

    private sealed class PreviewPopupState {
        public required Popup Popup { get; init; }
        public required Grid Root { get; init; }
        public required Path Arrow { get; init; }
        public required Path ActiveArrow { get; init; }
        public required Border ActiveBorder { get; init; }
    }

    public static readonly DependencyProperty ProfilesProperty =
        DependencyProperty.Register(
            nameof(Profiles),
            typeof(IReadOnlyList<ProfileModel>),
            typeof(ExpandedProfileSelectorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, OnProfilesChanged));

    public static readonly DependencyProperty SelectedProfileProperty =
        DependencyProperty.Register(
            nameof(SelectedProfile),
            typeof(ProfileModel),
            typeof(ExpandedProfileSelectorControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedProfileChanged));

    public ExpandedProfileSelectorControl() {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        MouseLeave += OnSelectorMouseLeave;
    }

    public IReadOnlyList<ProfileModel>? Profiles {
        get => (IReadOnlyList<ProfileModel>?)GetValue(ProfilesProperty);
        set => SetValue(ProfilesProperty, value);
    }

    public ProfileModel? SelectedProfile {
        get => (ProfileModel?)GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        _isLoaded = true;
        AttachViewModel(DataContext as MainViewModel);
        RebuildSelector();
        EnsurePreviewPopup();
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        UpdateSelectionVisual(immediate: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
        _isLoaded = false;
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        AttachViewModel(null);
        ClosePreview();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        AttachViewModel(e.NewValue as MainViewModel);
        RefreshPreviewRenderers();
    }

    private void AttachViewModel(MainViewModel? next) {
        if (_viewModel is not null) {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = next;

        if (_viewModel is not null) {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.UseBitmapProfileRenderer)) {
            RefreshPreviewRenderers();
        }
    }

    private static void OnProfilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var control = (ExpandedProfileSelectorControl)d;

        if (e.OldValue is INotifyCollectionChanged oldCollection) {
            oldCollection.CollectionChanged -= control.OnProfilesCollectionChanged;
        }

        if (e.NewValue is INotifyCollectionChanged newCollection) {
            newCollection.CollectionChanged += control.OnProfilesCollectionChanged;
        }

        control.RebuildSelector();
        control.UpdateSelectionVisual(immediate: true);
        control.ScheduleSelectionVisualUpdate();
    }

    private static void OnSelectedProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var control = (ExpandedProfileSelectorControl)d;
        control.UpdateSelectionVisual(immediate: !control._isLoaded);
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        RebuildSelector();
        UpdateSelectionVisual(immediate: true);
        ScheduleSelectionVisualUpdate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) {
        UpdateSelectionVisual(immediate: true);
    }

    private void RebuildSelector() {
        SegmentGrid.Children.Clear();
        SegmentGrid.ColumnDefinitions.Clear();
        _profileButtons.Clear();
        _iconGlows.Clear();
        _separatorTopHighlights.Clear();
        _separatorBottomHighlights.Clear();
        ClosePreview();

        var profiles = Profiles;
        if (profiles is null || profiles.Count == 0) {
            ActivePill.Visibility = Visibility.Collapsed;
            return;
        }

        for (var i = 0; i < profiles.Count; i++) {
            SegmentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var itemHost = BuildProfileHost(profiles[i], profiles.Count);
            Grid.SetColumn(itemHost, SegmentGrid.ColumnDefinitions.Count - 1);
            SegmentGrid.Children.Add(itemHost);

            if (i < profiles.Count - 1) {
                SegmentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SeparatorWidth) });

                var separator = BuildSeparator();
                Grid.SetColumn(separator, SegmentGrid.ColumnDefinitions.Count - 1);
                SegmentGrid.Children.Add(separator);
            }
        }

        RefreshPreviewRenderers();
        ScheduleSelectionVisualUpdate();
    }

    private Grid BuildProfileHost(ProfileModel profile, int count) {
        var host = new Grid {
            Height = ItemHeight,
            ClipToBounds = false
        };

        var button = new Button {
            Style = (Style)Resources["ProfileSelectorButtonStyle"],
            Tag = profile,
            IsEnabled = count > 1
        };

        button.Click += OnProfileButtonClick;
        button.MouseEnter += OnProfileButtonMouseEnter;
        button.MouseLeave += OnProfileButtonMouseLeave;
        button.MouseMove += OnProfileButtonMouseMove;

        var iconGlow = new Border {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")),
            Opacity = 0
        };
        iconGlow.Effect = new System.Windows.Media.Effects.BlurEffect {
            Radius = 6,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
        };

        var iconHost = new Grid {
            Width = 18,
            Height = 18
        };
        iconHost.Children.Add(iconGlow);

        var thumbnail = new Image {
            Source = profile.Thumbnail,
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform
        };
        iconHost.Children.Add(thumbnail);

        var textBlock = new TextBlock {
            Text = profile.Name,
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var content = new StackPanel {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(iconHost);
        content.Children.Add(new Border { Width = 8, Background = Brushes.Transparent });
        content.Children.Add(textBlock);

        button.Content = content;
        host.Children.Add(button);

        _profileButtons.Add(button);
        _iconGlows.Add(iconGlow);
        return host;
    }

    private void EnsurePreviewPopup() {
        if (_previewPopup is not null) {
            return;
        }

        _previewPopup = BuildPreviewPopup();
    }

    private PreviewPopupState BuildPreviewPopup() {
        var popup = new Popup {
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = LayoutRoot,
            StaysOpen = true,
            VerticalOffset = ItemHeight + PreviewVerticalOffset,
            IsOpen = false,
            PopupAnimation = PopupAnimation.Fade
        };

        var popupRoot = new Grid {
            Width = PreviewPopupWidth,
            IsHitTestVisible = false
        };

        var cardHost = new Grid {
            Margin = new Thickness(0, 10, 0, 0),
        };

        var card = new Border {
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F6292929")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#44FFFFFF")),
            BorderThickness = new Thickness(1.1),
            Padding = new Thickness(PreviewCardPadding)
        };

        cardHost.Effect = new System.Windows.Media.Effects.DropShadowEffect {
            BlurRadius = 30,
            ShadowDepth = 0,
            Color = Colors.Black,
            Opacity = 0.5
        };

        var activeBorder = new Border {
            CornerRadius = new CornerRadius(24),
            BorderThickness = new Thickness(1.25),
            Background = Brushes.Transparent,
            Opacity = 0,
            IsHitTestVisible = false,
            BorderBrush = new LinearGradientBrush {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new GradientStopCollection {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#B6B792FF"), 0.0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#CCFF63E3"), 0.5),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#B9FF73B9"), 1.0)
                }
            }
        };

        var cardStack = new StackPanel();

        _previewTitle = new TextBlock {
            Text = string.Empty,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78FFFFFF")),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        cardStack.Children.Add(_previewTitle);

        var visualHost = new Grid {
            Margin = new Thickness(0, 14, 0, 2),
            Width = PreviewVisualHostSize,
            Height = PreviewVisualHostSize,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var previewHalo = new Ellipse {
            Width = PreviewVisualHostSize,
            Height = PreviewVisualHostSize
        };
        previewHalo.Fill = new RadialGradientBrush {
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
        };
        visualHost.Children.Add(previewHalo);

        visualHost.Children.Add(new Ellipse {
            Width = 176,
            Height = 176,
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10FFFFFF")),
            StrokeThickness = 1
        });

        _previewVisual = new ProfileVisualControl {
            Width = 168,
            Height = 168,
            ProfileBlendProgress = 1.0,
            ModeProgress = 1.0,
            ImmersionValue = 1,
            RenderShadow = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        BindingOperations.SetBinding(
            _previewVisual,
            ProfileVisualControl.UseBitmapRendererProperty,
            new Binding("DataContext.UseBitmapProfileRenderer") { Source = this });

        visualHost.Children.Add(_previewVisual);
        cardStack.Children.Add(visualHost);
        card.Child = cardStack;
        cardHost.Children.Add(card);
        cardHost.Children.Add(activeBorder);

        var arrowGeometry = Geometry.Parse($"M 0,{PreviewArrowSize * 0.55} L {PreviewArrowSize * 0.5},0 L {PreviewArrowSize},{PreviewArrowSize * 0.55} Z");

        var arrow = new Path {
            Width = PreviewArrowSize,
            Height = PreviewArrowSize,
            Data = arrowGeometry,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2D2D2D")),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#52FFFFFF")),
            StrokeThickness = 1.15,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((PreviewPopupWidth - PreviewArrowSize) * 0.5, 3, 0, 0)
        };
        arrow.Effect = new System.Windows.Media.Effects.DropShadowEffect {
            BlurRadius = 10,
            ShadowDepth = 0,
            Color = Colors.Black,
            Opacity = 0.35
        };

        var activeArrow = new Path {
            Width = PreviewArrowSize,
            Height = PreviewArrowSize,
            Data = arrowGeometry,
            Fill = new LinearGradientBrush {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0.9),
                GradientStops = new GradientStopCollection {
                    new GradientStop((Color)ColorConverter.ConvertFromString("#B6B792FF"), 0.0),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#CCFF63E3"), 0.5),
                    new GradientStop((Color)ColorConverter.ConvertFromString("#B9FF73B9"), 1.0)
                }
            },
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#88FFFFFF")),
            StrokeThickness = 1.15,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness((PreviewPopupWidth - PreviewArrowSize) * 0.5, 3, 0, 0),
            Opacity = 0
        };
        activeArrow.Effect = arrow.Effect;

        popupRoot.Children.Add(arrow);
        popupRoot.Children.Add(activeArrow);
        popupRoot.Children.Add(cardHost);
        popup.Child = popupRoot;
        return new PreviewPopupState {
            Popup = popup,
            Root = popupRoot,
            Arrow = arrow,
            ActiveArrow = activeArrow,
            ActiveBorder = activeBorder
        };
    }

    private Grid BuildSeparator() {
        var separator = new Grid {
            Width = SeparatorWidth,
            Height = ItemHeight,
            IsHitTestVisible = false
        };

        var baseSlash = CreateSlashTextBlock(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF0A0A0A")));
        separator.Children.Add(baseSlash);

        var topHighlight = CreateSlashTextBlock((Brush)Resources["SlashHighlightBrush"]);
        topHighlight.Opacity = 0;
        separator.Children.Add(topHighlight);

        var bottomHighlight = CreateSlashTextBlock((Brush)Resources["SlashHighlightBrush"]);
        bottomHighlight.Opacity = 0;
        separator.Children.Add(bottomHighlight);

        separator.SizeChanged += (_, e) => {
            topHighlight.Clip = BuildSeparatorClip(e.NewSize, topHalf: true);
            bottomHighlight.Clip = BuildSeparatorClip(e.NewSize, topHalf: false);
        };

        _separatorTopHighlights.Add(topHighlight);
        _separatorBottomHighlights.Add(bottomHighlight);
        return separator;
    }

    private static TextBlock CreateSlashTextBlock(Brush foreground) {
        return new TextBlock {
            Text = "/",
            FontSize = 24,
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = foreground
        };
    }

    private static Geometry BuildSeparatorClip(Size size, bool topHalf) {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);

        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        if (topHalf) {
            context.BeginFigure(new Point(width * 0.44, 0), true, true);
            context.LineTo(new Point(width, 0), true, false);
            context.LineTo(new Point(width * 0.58, height), true, false);
            context.LineTo(new Point(0, height), true, false);
        } else {
            context.BeginFigure(new Point(0, 0), true, true);
            context.LineTo(new Point(width * 0.58, 0), true, false);
            context.LineTo(new Point(width, height), true, false);
            context.LineTo(new Point(width * 0.44, height), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private void OnProfileButtonClick(object sender, RoutedEventArgs e) {
        if (sender is Button button && button.Tag is ProfileModel profile) {
            SelectedProfile = profile;
        }
    }

    private void OnProfileButtonMouseEnter(object sender, System.Windows.Input.MouseEventArgs e) {
        if (sender is not Button button) {
            return;
        }

        if (button.Tag is not ProfileModel profile || Profiles is null || Profiles.Count <= 1) {
            return;
        }

        _hoveredProfileButton = button;
        _hoveredProfile = profile;
        CancelPendingPreviewClose();

        if (!IsPreviewModifierActive()) {
            ClosePreview();
            return;
        }

        ShowPreview(profile, e.GetPosition(LayoutRoot).X);
    }

    private void OnProfileButtonMouseMove(object sender, System.Windows.Input.MouseEventArgs e) {
        if (sender is not Button button) {
            return;
        }

        if (button.Tag is not ProfileModel profile) {
            return;
        }

        _hoveredProfileButton = button;
        _hoveredProfile = profile;
        CancelPendingPreviewClose();

        if (!IsPreviewModifierActive()) {
            ClosePreview();
            return;
        }

        if (_previewPopup is null || !_previewPopup.Popup.IsOpen) {
            ShowPreview(profile, e.GetPosition(LayoutRoot).X);
        } else {
            ShowPreview(profile, e.GetPosition(LayoutRoot).X);
        }
    }

    private void OnProfileButtonMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
        if (ReferenceEquals(sender, _hoveredProfileButton)) {
            _hoveredProfileButton = null;
            _hoveredProfile = null;
        }

        SchedulePreviewCloseIfNeeded();
    }

    private void OnSelectorMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) {
        ClosePreview();
    }

    private void ClosePreview() {
        CancelPendingPreviewClose();
        if (_previewPopup is not null) {
            _previewPopup.ActiveBorder.BeginAnimation(OpacityProperty, null);
            _previewPopup.ActiveBorder.Opacity = 0;
            _previewPopup.ActiveArrow.BeginAnimation(OpacityProperty, null);
            _previewPopup.ActiveArrow.Opacity = 0;
            _previewPopup.Popup.IsOpen = false;
        }

        _isPreviewAccentActive = false;
    }

    private void SchedulePreviewCloseIfNeeded() {
        CancelPendingPreviewClose();
        _pendingPreviewClose = Dispatcher.BeginInvoke(() => {
            _pendingPreviewClose = null;
            if (_hoveredProfileButton is not null || IsMouseOver) {
                return;
            }

            ClosePreview();
        }, DispatcherPriority.Input);
    }

    private void CancelPendingPreviewClose() {
        if (_pendingPreviewClose?.Status == DispatcherOperationStatus.Pending) {
            _pendingPreviewClose.Abort();
        }

        _pendingPreviewClose = null;
    }

    private void RefreshPreviewRenderers() {
        if (_previewPopup is not null) {
            _previewPopup.Root.InvalidateVisual();
        }
    }

    private void ShowPreview(ProfileModel profile, double cursorX) {
        EnsurePreviewPopup();
        if (_previewPopup is null || _previewVisual is null || _previewTitle is null) {
            return;
        }

        _previewTitle.Text = profile.Name;
        UpdatePreviewPopupPosition(cursorX);
        _previewPopup.Popup.IsOpen = true;
        UpdatePreviewAccent(profile);
        AnimatePreviewProfile(profile);
    }

    private void UpdatePreviewPopupPosition(double cursorX) {
        if (_previewPopup is null || LayoutRoot.ActualWidth <= 0) {
            return;
        }

        var popupState = _previewPopup;
        var popupWidth = popupState.Root.Width > 0 ? popupState.Root.Width : PreviewPopupWidth;
        var maxLeft = Math.Max(0, LayoutRoot.ActualWidth - popupWidth);
        var popupLeft = Math.Clamp(cursorX - (popupWidth * 0.5), 0, maxLeft);
        var arrowLeft = Math.Clamp(
            cursorX - popupLeft - (PreviewArrowSize * 0.5),
            PreviewArrowEdgePadding,
            popupWidth - PreviewArrowSize - PreviewArrowEdgePadding);

        popupState.Popup.HorizontalOffset = popupLeft;
        popupState.Arrow.Margin = new Thickness(arrowLeft, 0, 0, 0);
        popupState.ActiveArrow.Margin = new Thickness(arrowLeft, 0, 0, 0);
    }

    private void UpdatePreviewAccent(ProfileModel hoveredProfile) {
        if (_previewPopup is null) {
            return;
        }

        var isActiveProfile = SelectedProfile is not null
            && (ReferenceEquals(SelectedProfile, hoveredProfile) || SelectedProfile.Name == hoveredProfile.Name);

        if (isActiveProfile == _isPreviewAccentActive) {
            return;
        }

        _isPreviewAccentActive = isActiveProfile;

        _previewPopup.ActiveBorder.BeginAnimation(OpacityProperty, null);
        _previewPopup.ActiveArrow.BeginAnimation(OpacityProperty, null);
        var animation = new DoubleAnimation {
            To = isActiveProfile ? 1.0 : 0.0,
            Duration = TimeSpan.FromMilliseconds(isActiveProfile ? 180 : 240),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _previewPopup.ActiveBorder.BeginAnimation(OpacityProperty, animation);
        _previewPopup.ActiveArrow.BeginAnimation(OpacityProperty, animation);
    }

    private void OnPreProcessInput(object? sender, PreProcessInputEventArgs e) {
        if (!IsPreviewModifierActive()) {
            ClosePreview();
            return;
        }

        if (_hoveredProfile is null || _hoveredProfileButton is null || !_hoveredProfileButton.IsMouseOver) {
            return;
        }

        var cursorX = Mouse.GetPosition(LayoutRoot).X;

        if (_previewPopup is null || !_previewPopup.Popup.IsOpen) {
            ShowPreview(_hoveredProfile, cursorX);
            return;
        }

        UpdatePreviewPopupPosition(cursorX);
    }

    private static bool IsPreviewModifierActive() {
        var modifiers = Keyboard.Modifiers;
        return modifiers.HasFlag(ModifierKeys.Shift) || modifiers.HasFlag(ModifierKeys.Control);
    }

    private void AnimatePreviewProfile(ProfileModel targetProfile) {
        if (_previewVisual is null) {
            return;
        }

        _previewVisual.BeginAnimation(ProfileVisualControl.ProfileBlendProgressProperty, null);

        var currentVisualProfile = CaptureCurrentPreviewProfile();
        if (currentVisualProfile is null) {
            _previewVisual.FromProfile = targetProfile;
            _previewVisual.ToProfile = targetProfile;
            _previewVisual.ProfileBlendProgress = 1.0;
            return;
        }

        if (ReferenceEquals(currentVisualProfile, targetProfile) || currentVisualProfile.Name == targetProfile.Name) {
            _previewVisual.FromProfile = targetProfile;
            _previewVisual.ToProfile = targetProfile;
            _previewVisual.ProfileBlendProgress = 1.0;
            return;
        }

        _previewVisual.FromProfile = currentVisualProfile;
        _previewVisual.ToProfile = targetProfile;
        _previewVisual.ProfileBlendProgress = 0.0;

        var animation = new DoubleAnimation {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) => {
            if (_previewVisual is null) {
                return;
            }

            _previewVisual.BeginAnimation(ProfileVisualControl.ProfileBlendProgressProperty, null);
            _previewVisual.FromProfile = targetProfile;
            _previewVisual.ToProfile = targetProfile;
            _previewVisual.ProfileBlendProgress = 1.0;
        };
        _previewVisual.BeginAnimation(ProfileVisualControl.ProfileBlendProgressProperty, animation);
    }

    private ProfileModel? CaptureCurrentPreviewProfile() {
        if (_previewVisual is null) {
            return null;
        }

        var fromProfile = _previewVisual.FromProfile ?? _previewVisual.ToProfile;
        var toProfile = _previewVisual.ToProfile ?? _previewVisual.FromProfile;
        if (fromProfile is null || toProfile is null) {
            return null;
        }

        var blend = Math.Clamp(_previewVisual.ProfileBlendProgress, 0.0, 1.0);
        if (ReferenceEquals(fromProfile, toProfile) || blend <= 0.0001) {
            return fromProfile;
        }

        if (blend >= 0.9999) {
            return toProfile;
        }

        return BlendProfiles(fromProfile, toProfile, blend);
    }

    private void UpdateSelectionVisual(bool immediate) {
        if (!_isLoaded || Profiles is null || Profiles.Count == 0 || SelectedProfile is null) {
            ActivePill.Visibility = Visibility.Collapsed;
            return;
        }

        var selectedIndex = FindProfileIndex(Profiles, SelectedProfile);
        if (selectedIndex < 0 || selectedIndex >= _profileButtons.Count) {
            ActivePill.Visibility = Visibility.Collapsed;
            return;
        }

        var selectedButton = _profileButtons[selectedIndex];
        if (selectedButton.ActualWidth <= 0 || selectedButton.ActualHeight <= 0) {
            ScheduleSelectionVisualUpdate();
            return;
        }

        var bounds = selectedButton.TransformToAncestor(LayoutRoot)
            .TransformBounds(new Rect(0, 0, selectedButton.ActualWidth, selectedButton.ActualHeight));

        SelectionCanvas.Width = LayoutRoot.ActualWidth;
        SelectionCanvas.Height = LayoutRoot.ActualHeight;

        ActivePill.Visibility = Visibility.Visible;
        ActivePill.Height = bounds.Height;
        ActivePill.Width = bounds.Width;
        ActivePill.Clip = BuildSegmentClip(bounds.Size, selectedIndex, Profiles.Count);
        Canvas.SetTop(ActivePill, bounds.Top);

        var targetX = bounds.Left;
        var currentX = ActivePillTransform.X;

        ActivePillTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ActivePillTransform.X = currentX;
        if (immediate) {
            _selectionAnimationSourceIndex = null;
            _selectionAnimationTargetIndex = null;
            ActivePillTransform.X = targetX;
        } else {
            _selectionAnimationSourceIndex = FindNearestProfileIndex(currentX);
            _selectionAnimationTargetIndex = selectedIndex;
            var animation = new DoubleAnimation {
                From = currentX,
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new BackEase {
                    EasingMode = EasingMode.EaseOut,
                    Amplitude = 0.18
                }
            };
            animation.Completed += (_, _) => {
                _selectionAnimationSourceIndex = null;
                _selectionAnimationTargetIndex = null;
                UpdateSeparatorHighlights(selectedIndex, immediate: false);
            };
            ActivePillTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        UpdateButtonVisualStates(selectedIndex);
        var separatorUpdateImmediate = immediate || (!immediate && _selectionAnimationSourceIndex.HasValue);
        UpdateSeparatorHighlights(
            selectedIndex,
            separatorUpdateImmediate,
            !immediate ? _selectionAnimationSourceIndex : null);
    }

    private void ScheduleSelectionVisualUpdate() {
        if (!IsLoaded) {
            return;
        }

        Dispatcher.BeginInvoke(
            () => UpdateSelectionVisual(immediate: true),
            DispatcherPriority.Loaded);
    }

    private void UpdateButtonVisualStates(int selectedIndex) {
        for (var i = 0; i < _profileButtons.Count; i++) {
            if (_profileButtons[i].Content is not StackPanel content || content.Children.Count < 3) {
                continue;
            }

            var isSelected = i == selectedIndex;
            if (content.Children[2] is TextBlock text) {
                text.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isSelected ? "#FFFFFFFF" : "#9EFFFFFF"));
            }

            if (content.Children[0] is Grid iconHost && iconHost.Children.Count >= 1 && iconHost.Children[0] is Border glow) {
                glow.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                    isSelected ? "#38FFFFFF" : "#1AFF57B9"));
                glow.Opacity = isSelected ? 1 : 0.7;
            }
        }
    }

    private int FindNearestProfileIndex(double pillX) {
        if (_profileButtons.Count == 0) {
            return -1;
        }

        var nearestIndex = 0;
        var nearestDistance = double.MaxValue;

        for (var i = 0; i < _profileButtons.Count; i++) {
            var button = _profileButtons[i];
            if (button.ActualWidth <= 0) {
                continue;
            }

            var bounds = button.TransformToAncestor(LayoutRoot)
                .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
            var distance = Math.Abs(bounds.Left - pillX);
            if (distance < nearestDistance) {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    private void UpdateSeparatorHighlights(int selectedIndex, bool immediate, int? additionalHighlightedIndex = null) {
        for (var i = 0; i < _separatorTopHighlights.Count; i++) {
            var isHighlighted = additionalHighlightedIndex.HasValue
                ? IsSeparatorBetween(additionalHighlightedIndex.Value, selectedIndex, i)
                : i == selectedIndex || i + 1 == selectedIndex;
            var targetOpacity = isHighlighted ? 1 : 0;
            AnimateOpacity(_separatorTopHighlights[i], targetOpacity, immediate);
            AnimateOpacity(_separatorBottomHighlights[i], targetOpacity, immediate);
        }
    }

    private static bool IsSeparatorBetween(int sourceIndex, int targetIndex, int separatorIndex) {
        if (sourceIndex == targetIndex) {
            return separatorIndex == targetIndex || separatorIndex + 1 == targetIndex;
        }

        var minIndex = Math.Min(sourceIndex, targetIndex);
        var maxIndex = Math.Max(sourceIndex, targetIndex);
        return separatorIndex >= minIndex && separatorIndex < maxIndex;
    }

    private static void AnimateOpacity(UIElement element, double targetOpacity, bool immediate) {
        element.BeginAnimation(OpacityProperty, null);
        if (immediate) {
            element.Opacity = targetOpacity;
            return;
        }

        var animation = new DoubleAnimation {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private static Geometry BuildSegmentClip(Size size, int index, int count) {
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);
        var cut = Math.Min(SlashCut, width * 0.25);
        var radius = Math.Min(18.0, Math.Min(width * 0.5, height * 0.5));

        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        if (count <= 1) {
            context.BeginFigure(new Point(radius, 0), true, true);
            context.LineTo(new Point(width - radius, 0), true, false);
            context.ArcTo(new Point(width, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(width, height - radius), true, false);
            context.ArcTo(new Point(width - radius, height), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(radius, height), true, false);
            context.ArcTo(new Point(0, height - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(0, radius), true, false);
            context.ArcTo(new Point(radius, 0), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        } else if (index == 0) {
            context.BeginFigure(new Point(radius, 0), true, true);
            context.LineTo(new Point(width, 0), true, false);
            context.LineTo(new Point(width - cut, height), true, false);
            context.LineTo(new Point(radius, height), true, false);
            context.ArcTo(new Point(0, height - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(0, radius), true, false);
            context.ArcTo(new Point(radius, 0), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        } else if (index == count - 1) {
            context.BeginFigure(new Point(cut, 0), true, true);
            context.LineTo(new Point(width - radius, 0), true, false);
            context.ArcTo(new Point(width, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(width, height - radius), true, false);
            context.ArcTo(new Point(width - radius, height), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            context.LineTo(new Point(0, height), true, false);
        } else {
            context.BeginFigure(new Point(cut, 0), true, true);
            context.LineTo(new Point(width, 0), true, false);
            context.LineTo(new Point(width - cut, height), true, false);
            context.LineTo(new Point(0, height), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static ProfileModel BlendProfiles(ProfileModel fromProfile, ProfileModel toProfile, double blend) {
        var leftData = BlendValues(fromProfile.LeftData, toProfile.LeftData, blend);
        var rightData = BlendValues(fromProfile.RightData, toProfile.RightData, blend);
        var colour = Lerp(fromProfile.Colour, toProfile.Colour, blend);
        return new ProfileModel(toProfile.Name, colour, leftData, rightData);
    }

    private static double[] BlendValues(IReadOnlyList<double> fromValues, IReadOnlyList<double> toValues, double blend) {
        var count = Math.Min(fromValues.Count, toValues.Count);
        var values = new double[count];

        for (var i = 0; i < count; i++) {
            values[i] = Lerp(fromValues[i], toValues[i], blend);
        }

        return values;
    }

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static int FindProfileIndex(IReadOnlyList<ProfileModel> profiles, ProfileModel selectedProfile) {
        for (var i = 0; i < profiles.Count; i++) {
            if (ReferenceEquals(profiles[i], selectedProfile) || Equals(profiles[i], selectedProfile)) {
                return i;
            }
        }

        return -1;
    }
}
