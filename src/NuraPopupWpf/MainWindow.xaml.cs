using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using NuraPopupWpf.Models;
using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf;

public partial class MainWindow : Window {
    private const double WindowMargin = 20.0;
    private const double CollapsedShellWidth = 390.0;
    private const double CollapsedShellHeight = 760.0;
    private const double ExpandedShellWidth = 980.0;
    private const double ExpandedShellHeight = 800.0;
    private const double OuterMarginSize = 40.0;
    private const uint MonitorDefaultToNearest = 2;

    private MainViewModel? _viewModel;
    private bool _isApplyingPlacement;
    private bool _hasManualAnchorOverride;
    private Point _manualAnchorCenter;
    private bool _isRememberedPlacementAnimationActive;
    private Rect _rememberedPlacementInsetArea;
    private Point _rememberedPlacementCompactPosition;
    private RememberPlacementSide _rememberedPlacementSide;

    private enum TaskbarEdge {
        Left,
        Top,
        Right,
        Bottom
    }

    private enum RememberPlacementSide {
        Left,
        Right
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint {
        public int X;
        public int Y;

        public NativePoint(Point point) {
            X = (int)Math.Round(point.X);
            Y = (int)Math.Round(point.Y);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    private readonly record struct MonitorAreas(Rect MonitorArea, Rect WorkArea);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    public MainWindow() {
        InitializeComponent();
        DataContext = new MainViewModel();

        Loaded += OnLoaded;
        DataContextChanged += OnWindowDataContextChanged;
    }

    protected override void OnSourceInitialized(EventArgs e) {
        base.OnSourceInitialized(e);

        var source = (System.Windows.Interop.HwndSource)
            PresentationSource.FromVisual(this);

        source.AddHook(WndProc);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        AttachViewModel(DataContext as MainViewModel);
        ApplyInitialWindowSize();

        Dispatcher.BeginInvoke(
            () => ApplyWindowPlacement(),
            DispatcherPriority.Loaded);
    }

    private void OnWindowDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        AttachViewModel(e.NewValue as MainViewModel);
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
        if (_viewModel is null) {
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.SelectedWindowAnchorModeValue) ||
            e.PropertyName == nameof(MainViewModel.SelectedWindowAnchorEdgeValue)) {
            _hasManualAnchorOverride = false;

            if (e.PropertyName == nameof(MainViewModel.SelectedWindowAnchorModeValue) &&
                _viewModel.SelectedWindowAnchorModeValue == WindowAnchorMode.RememberLastPosition) {
                EnsureRememberedCompactPositionCaptured();
            }

            Dispatcher.BeginInvoke(
                () => AnimateWindowForCurrentState(),
                DispatcherPriority.Loaded);
        }

        if (e.PropertyName == nameof(MainViewModel.IsExpanded)) {
            Dispatcher.BeginInvoke(
                () => AnimateWindowForCurrentState(),
                DispatcherPriority.Loaded);
        }
    }

#region Titlebar Dragging
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState != MouseButtonState.Pressed) {
            return;
        }

        if (e.OriginalSource is DependencyObject source &&
            IsInteractiveElement(source)) {
            return;
        }

        BeginWindowDrag();
    }

    private void ShellBackground_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState != MouseButtonState.Pressed || e.Handled) {
            return;
        }

        if (e.OriginalSource is not DependencyObject source || IsInteractiveElement(source)) {
            return;
        }

        BeginWindowDrag();
        e.Handled = true;
    }

    private const int WM_EXITSIZEMOVE = 0x0232;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }


    double drag_startingLeft = 0;
    double drag_startingTop = 0;

    private void BeginWindowDrag() {
        drag_startingLeft = Left;
        drag_startingTop = Top;

        DragMove();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,  IntPtr lParam, ref bool handled) {
        if (msg == WM_EXITSIZEMOVE) {
            // We need to get the location of the rect as in the current life cycle WPF
            //   still has not updated the Top and Left properties.
            GetWindowRect(hwnd, out var rect);

            // GetWindowRect returns physical pixels.
            //   WPF Window.Left/Top are device-independent pixels.
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

            var topLeft = transform.Transform(new Point(rect.Left, rect.Top));

            var finalLeft = topLeft.X;
            var finalTop = topLeft.Y;

            var absLeft = Math.Abs(finalLeft - drag_startingLeft);
            var absTop = Math.Abs(finalTop - drag_startingTop);

            Dispatcher.BeginInvoke(() =>
            {
                HandleManualDragCompleted(
                    drag_startingLeft,
                    drag_startingTop,
                    new Rect(finalLeft, finalTop, Width, Height));
            }, DispatcherPriority.Background);
        }

        return IntPtr.Zero;
    }


    private void HandleManualDragCompleted(double startingLeft, double startingTop, Rect Location) {
        if (_viewModel is null || _isApplyingPlacement) {
            return;
        }

        var absLeft = Math.Abs(Location.Left - startingLeft);
        var absTop = Math.Abs(Location.Top - startingTop);

        var moved = absLeft > 0.5 || absTop > 0.5;
        if (!moved) {
            return;
        }

        if (_viewModel.SelectedWindowAnchorModeValue == WindowAnchorMode.RememberLastPosition) {
            var rememberedPosition = GetCompactPositionForCurrentWindowRect(Location, useSavedBasedOnPosition: false);
            _viewModel.SaveRememberedWindowPosition(rememberedPosition.X, rememberedPosition.Y);
            return;
        }

        _hasManualAnchorOverride = true;
        _manualAnchorCenter = GetWindowCenter();
    }

    private static bool IsInteractiveElement(DependencyObject source) {
        return FindAncestor<ButtonBase>(source) is not null ||
               FindAncestor<ListBoxItem>(source) is not null ||
               FindAncestor<Selector>(source) is not null ||
               FindAncestor<ScrollBar>(source) is not null ||
               FindAncestor<Thumb>(source) is not null ||
               FindAncestor<Slider>(source) is not null ||
               FindAncestor<TextBox>(source) is not null ||
               FindAncestor<PasswordBox>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject {
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
        return current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : null;
    }
#endregion

    private void ApplyInitialWindowSize() {
        var size = GetTargetWindowSize();
        Width = size.Width;
        Height = size.Height;
    }

    private void ApplyWindowPlacement() {
        if (!IsLoaded || _viewModel is null) {
            return;
        }

        _isApplyingPlacement = true;

        try {
            StopRememberedPlacementAnimation();
            var targetSize = GetTargetWindowSize();
            var targetRect = GetTargetWindowRect(targetSize);
            CommitCurrentPresentationValues();
            Left = targetRect.Left;
            Top = targetRect.Top;
            Width = targetRect.Width;
            Height = targetRect.Height;
        } finally {
            _isApplyingPlacement = false;
        }
    }

    private void AnimateWindowForCurrentState() {
        if (!IsLoaded || _viewModel is null || _isApplyingPlacement) {
            return;
        }

        CommitCurrentPresentationValues();

        var targetSize = GetTargetWindowSize();
        var targetRect = GetTargetWindowRect(targetSize);
        var duration = TimeSpan.FromMilliseconds(_viewModel.IsExpanded ? 280 : 250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        if (_viewModel.SelectedWindowAnchorModeValue == WindowAnchorMode.RememberLastPosition &&
            TryStartRememberedPlacementAnimation(targetRect, duration, ease)) {
            return;
        }

        StopRememberedPlacementAnimation();
        BeginAnimation(WidthProperty, new DoubleAnimation(targetRect.Width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(targetRect.Height, duration) { EasingFunction = ease });
        BeginAnimation(LeftProperty, new DoubleAnimation(targetRect.Left, duration) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(targetRect.Top, duration) { EasingFunction = ease });
    }

    private Rect GetTargetWindowRect(Size targetSize) {
        if (_viewModel is null) {
            return new Rect(Left, Top, targetSize.Width, targetSize.Height);
        }

        if (_hasManualAnchorOverride && _viewModel.SelectedWindowAnchorModeValue != WindowAnchorMode.RememberLastPosition) {
            return BuildCenteredRect(_manualAnchorCenter, targetSize, GetMonitorAreasForPoint(_manualAnchorCenter).WorkArea);
        }

        return _viewModel.SelectedWindowAnchorModeValue switch {
            WindowAnchorMode.AnchorEdge => BuildAnchorEdgeRect(targetSize),
            WindowAnchorMode.Taskbar => BuildTaskbarRect(targetSize),
            WindowAnchorMode.RememberLastPosition => BuildRememberedRect(targetSize),
            _ => BuildAnchorEdgeRect(targetSize)
        };
    }

    private Rect BuildAnchorEdgeRect(Size targetSize) {
        var monitorAreas = GetCurrentMonitorAreas();
        var insetArea = GetInsetArea(monitorAreas.WorkArea);
        var anchorPoint = GetAnchorPoint(insetArea, _viewModel?.SelectedWindowAnchorEdgeValue ?? WindowAnchorEdge.Center);

        var targetLeft = GetHorizontalCoordinate(anchorPoint.X, targetSize.Width, GetHorizontalAnchor(_viewModel?.SelectedWindowAnchorEdgeValue ?? WindowAnchorEdge.Center));
        var targetTop = GetVerticalCoordinate(anchorPoint.Y, targetSize.Height, GetVerticalAnchor(_viewModel?.SelectedWindowAnchorEdgeValue ?? WindowAnchorEdge.Center));

        return ClampRectToArea(new Rect(targetLeft, targetTop, targetSize.Width, targetSize.Height), insetArea);
    }

    private Rect BuildTaskbarRect(Size targetSize) {
        var monitorAreas = GetCurrentMonitorAreas();
        var edge = DetectTaskbarEdge(monitorAreas);

        double targetLeft;
        double targetTop;

        switch (edge) {
            case TaskbarEdge.Left:
                targetLeft = monitorAreas.WorkArea.Left + WindowMargin;
                targetTop = monitorAreas.WorkArea.Bottom - targetSize.Height - WindowMargin;
                break;

            case TaskbarEdge.Top:
                targetLeft = monitorAreas.WorkArea.Right - targetSize.Width - WindowMargin;
                targetTop = monitorAreas.WorkArea.Top + WindowMargin;
                break;

            case TaskbarEdge.Right:
                targetLeft = monitorAreas.WorkArea.Right - targetSize.Width - WindowMargin;
                targetTop = monitorAreas.WorkArea.Bottom - targetSize.Height - WindowMargin;
                break;

            default:
                targetLeft = monitorAreas.WorkArea.Right - targetSize.Width - WindowMargin;
                targetTop = monitorAreas.WorkArea.Bottom - targetSize.Height - WindowMargin;
                break;
        }

        return ClampRectToArea(new Rect(targetLeft, targetTop, targetSize.Width, targetSize.Height), GetInsetArea(monitorAreas.WorkArea));
    }

    private Rect BuildRememberedRect(Size targetSize) {
        var collapsedSize = GetCollapsedWindowSize();
        var compactPosition = GetRememberedCompactAnchorPosition();
        if (compactPosition is null) {
            return BuildAnchorEdgeRect(targetSize);
        }

        var compactCenter = new Point(
            compactPosition.Value.X + (collapsedSize.Width * 0.5),
            compactPosition.Value.Y + (collapsedSize.Height * 0.5));
        var workArea = GetMonitorAreasForPoint(compactCenter).WorkArea;
        var insetArea = GetInsetArea(workArea);

        var targetLeft = _viewModel?.SelectedRememberExpandTypeValue switch {
            RememberExpandType.Left => compactPosition.Value.X + collapsedSize.Width - targetSize.Width,
            RememberExpandType.Right => compactPosition.Value.X,
            _ => compactCenter.X <= GetWorkAreaCenter(workArea).X
                ? compactPosition.Value.X
                : compactPosition.Value.X + collapsedSize.Width - targetSize.Width
        };

        var targetTop = compactPosition.Value.Y;
        return ClampRectToArea(new Rect(targetLeft, targetTop, targetSize.Width, targetSize.Height), insetArea);
    }

    private static Rect BuildCenteredRect(Point center, Size targetSize, Rect workArea) {
        var rect = new Rect(
            center.X - (targetSize.Width * 0.5),
            center.Y - (targetSize.Height * 0.5),
            targetSize.Width,
            targetSize.Height);

        return ClampRectToArea(rect, workArea);
    }

    private static Rect ClampRectToArea(Rect rect, Rect area) {
        var clampedLeft = ClampToArea(rect.Left, area.Left, area.Right - rect.Width);
        var clampedTop = ClampToArea(rect.Top, area.Top, area.Bottom - rect.Height);
        return new Rect(clampedLeft, clampedTop, rect.Width, rect.Height);
    }

    private static double ClampToArea(double value, double min, double max) {
        if (max < min) {
            return min;
        }

        return Math.Clamp(value, min, max);
    }

    private static Point GetWorkAreaCenter(Rect workArea) {
        return new Point(
            workArea.Left + (workArea.Width * 0.5),
            workArea.Top + (workArea.Height * 0.5));
    }

    private Point GetWindowCenter() {
        return new Point(Left + (ActualWidth * 0.5), Top + (ActualHeight * 0.5));
    }

    private bool TryStartRememberedPlacementAnimation(Rect targetRect, TimeSpan duration, IEasingFunction easingFunction) {
        var compactPosition = GetRememberedCompactAnchorPosition();
        if (compactPosition is null || _viewModel is null) {
            return false;
        }

        var collapsedSize = GetCollapsedWindowSize();
        var compactCenter = new Point(
            compactPosition.Value.X + (collapsedSize.Width * 0.5),
            compactPosition.Value.Y + (collapsedSize.Height * 0.5));
        var workArea = GetMonitorAreasForPoint(compactCenter).WorkArea;
        var insetArea = GetInsetArea(workArea);
        var currentRect = new Rect(Left, Top, Width, Height);
        var side = _viewModel.IsExpanded
            ? ResolveRememberPlacementSide(compactPosition.Value, workArea)
            : InferCurrentRememberPlacementSide(compactPosition.Value, currentRect);

        StopRememberedPlacementAnimation();

        _isRememberedPlacementAnimationActive = true;
        _rememberedPlacementInsetArea = insetArea;
        _rememberedPlacementCompactPosition = compactPosition.Value;
        _rememberedPlacementSide = side;

        CompositionTarget.Rendering += OnRememberedPlacementRendering;
        Top = targetRect.Top;
        UpdateRememberedPlacementLeft();

        var widthAnimation = new DoubleAnimation(targetRect.Width, duration) { EasingFunction = easingFunction };
        widthAnimation.Completed += (_, _) => {
            StopRememberedPlacementAnimation();
            Left = targetRect.Left;
            Top = targetRect.Top;
            Width = targetRect.Width;
            Height = targetRect.Height;
        };

        BeginAnimation(WidthProperty, widthAnimation);
        BeginAnimation(HeightProperty, new DoubleAnimation(targetRect.Height, duration) { EasingFunction = easingFunction });
        return true;
    }

    private void OnRememberedPlacementRendering(object? sender, EventArgs e) {
        if (!_isRememberedPlacementAnimationActive) {
            return;
        }

        UpdateRememberedPlacementLeft();
    }

    private void UpdateRememberedPlacementLeft() {
        var collapsedSize = GetCollapsedWindowSize();
        var computedLeft = _rememberedPlacementSide switch {
            RememberPlacementSide.Left => _rememberedPlacementCompactPosition.X + collapsedSize.Width - Width,
            _ => _rememberedPlacementCompactPosition.X
        };

        Left = ClampToArea(computedLeft, _rememberedPlacementInsetArea.Left, _rememberedPlacementInsetArea.Right - Width);
    }

    private void StopRememberedPlacementAnimation() {
        if (!_isRememberedPlacementAnimationActive) {
            return;
        }

        _isRememberedPlacementAnimationActive = false;
        CompositionTarget.Rendering -= OnRememberedPlacementRendering;
    }

    private RememberPlacementSide ResolveRememberPlacementSide(Point compactPosition, Rect workArea) {
        var collapsedSize = GetCollapsedWindowSize();
        var compactCenterX = compactPosition.X + (collapsedSize.Width * 0.5);

        return _viewModel?.SelectedRememberExpandTypeValue switch {
            RememberExpandType.Left => RememberPlacementSide.Left,
            RememberExpandType.Right => RememberPlacementSide.Right,
            _ => compactCenterX <= GetWorkAreaCenter(workArea).X
                ? RememberPlacementSide.Right
                : RememberPlacementSide.Left
        };
    }

    private RememberPlacementSide InferCurrentRememberPlacementSide(Point compactPosition, Rect currentRect) {
        var collapsedSize = GetCollapsedWindowSize();
        var expectedRightLeft = compactPosition.X;
        var expectedLeftLeft = compactPosition.X + collapsedSize.Width - currentRect.Width;

        return Math.Abs(currentRect.Left - expectedLeftLeft) < Math.Abs(currentRect.Left - expectedRightLeft)
            ? RememberPlacementSide.Left
            : RememberPlacementSide.Right;
    }

    private Point? GetRememberedCompactAnchorPosition() {
        var collapsedSize = GetCollapsedWindowSize();
        if (IsCurrentWindowCollapsed(collapsedSize)) {
            return new Point(Left, Top);
        }

        if (_viewModel is not null && _viewModel.TryGetRememberedWindowPosition(out var savedPosition)) {
            return savedPosition;
        }

        if (_viewModel is null) {
            return null;
        }

        return GetCompactPositionForCurrentWindowRect(new Rect(Left, Top, Width, Height), useSavedBasedOnPosition: false);
    }

    private bool IsCurrentWindowCollapsed(Size collapsedSize) {
        return Math.Abs(Width - collapsedSize.Width) < 0.5 &&
               Math.Abs(Height - collapsedSize.Height) < 0.5;
    }

    private void CommitCurrentPresentationValues() {
        StopRememberedPlacementAnimation();

        var currentLeft = Left;
        var currentTop = Top;
        var currentWidth = Width;
        var currentHeight = Height;

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);

        Left = currentLeft;
        Top = currentTop;
        Width = currentWidth;
        Height = currentHeight;
    }

    private void EnsureRememberedCompactPositionCaptured() {
        if (_viewModel is null || _viewModel.TryGetRememberedWindowPosition(out _)) {
            return;
        }

        CommitCurrentPresentationValues();
        var compactPosition = GetCompactPositionForCurrentWindowRect(new Rect(Left, Top, Width, Height), useSavedBasedOnPosition: false);
        _viewModel.SaveRememberedWindowPosition(compactPosition.X, compactPosition.Y);
    }

    private Point GetCompactPositionForCurrentWindowRect(Rect targetRect, bool useSavedBasedOnPosition = true) {
        if (_viewModel is null) {
            return new Point(targetRect.Left, targetRect.Top);
        }

        var collapsedSize = GetCollapsedWindowSize();
        if (Math.Abs(targetRect.Width - collapsedSize.Width) < 0.5) {
            return new Point(targetRect.Left, targetRect.Top);
        }

        var compactLeft = _viewModel.SelectedRememberExpandTypeValue switch {
            RememberExpandType.Left => targetRect.Left + targetRect.Width - collapsedSize.Width,
            RememberExpandType.Right => targetRect.Left,
            _ => ResolveBasedOnPositionCompactLeft(targetRect, collapsedSize.Width, useSavedBasedOnPosition),
        };

        return new Point(compactLeft, targetRect.Top);
    }

    private double ResolveBasedOnPositionCompactLeft(Rect targetRect, double collapsedWidth, bool useSavedBasedOnPosition) {
        var expandRightCompactLeft = targetRect.Left;
        var expandLeftCompactLeft = targetRect.Left + targetRect.Width - collapsedWidth;
        var targetRectCenter = new Point(
            targetRect.Left + (targetRect.Width * 0.5),
            targetRect.Top + (targetRect.Height * 0.5));
        var workArea = GetMonitorAreasForPoint(targetRectCenter).WorkArea;
        var workAreaCenterX = GetWorkAreaCenter(workArea).X;

        if (useSavedBasedOnPosition &&
            _viewModel is not null &&
            _viewModel.TryGetRememberedWindowPosition(out var savedCompactPosition)) {
            var savedCompactCenterX = savedCompactPosition.X + (collapsedWidth * 0.5);
            return savedCompactCenterX <= workAreaCenterX
                ? expandRightCompactLeft
                : expandLeftCompactLeft;
        }

        return targetRectCenter.X <= workAreaCenterX
            ? expandRightCompactLeft
            : expandLeftCompactLeft;
    }

    private Size GetTargetWindowSize() {
        var shellWidth = _viewModel?.IsExpanded == true ? ExpandedShellWidth : CollapsedShellWidth;
        var shellHeight = _viewModel?.IsExpanded == true ? ExpandedShellHeight : CollapsedShellHeight;
        return new Size(shellWidth + OuterMarginSize, shellHeight + OuterMarginSize);
    }

    private static Size GetCollapsedWindowSize() => new(CollapsedShellWidth + OuterMarginSize, CollapsedShellHeight + OuterMarginSize);

    private static Rect GetInsetArea(Rect workArea) {
        var insetWidth = Math.Max(0, workArea.Width - (WindowMargin * 2));
        var insetHeight = Math.Max(0, workArea.Height - (WindowMargin * 2));
        return new Rect(workArea.Left + WindowMargin, workArea.Top + WindowMargin, insetWidth, insetHeight);
    }

    private static Point GetAnchorPoint(Rect area, WindowAnchorEdge edge) {
        return edge switch {
            WindowAnchorEdge.TopLeft => new Point(area.Left, area.Top),
            WindowAnchorEdge.TopCenter => new Point(area.Left + (area.Width * 0.5), area.Top),
            WindowAnchorEdge.TopRight => new Point(area.Right, area.Top),
            WindowAnchorEdge.MiddleLeft => new Point(area.Left, area.Top + (area.Height * 0.5)),
            WindowAnchorEdge.Center => new Point(area.Left + (area.Width * 0.5), area.Top + (area.Height * 0.5)),
            WindowAnchorEdge.MiddleRight => new Point(area.Right, area.Top + (area.Height * 0.5)),
            WindowAnchorEdge.BottomLeft => new Point(area.Left, area.Bottom),
            WindowAnchorEdge.BottomCenter => new Point(area.Left + (area.Width * 0.5), area.Bottom),
            WindowAnchorEdge.BottomRight => new Point(area.Right, area.Bottom),
            _ => new Point(area.Left + (area.Width * 0.5), area.Top + (area.Height * 0.5))
        };
    }

    private static HorizontalAnchor GetHorizontalAnchor(WindowAnchorEdge edge) => edge switch {
        WindowAnchorEdge.TopLeft or WindowAnchorEdge.MiddleLeft or WindowAnchorEdge.BottomLeft => HorizontalAnchor.Left,
        WindowAnchorEdge.TopRight or WindowAnchorEdge.MiddleRight or WindowAnchorEdge.BottomRight => HorizontalAnchor.Right,
        _ => HorizontalAnchor.Center
    };

    private static VerticalAnchor GetVerticalAnchor(WindowAnchorEdge edge) => edge switch {
        WindowAnchorEdge.TopLeft or WindowAnchorEdge.TopCenter or WindowAnchorEdge.TopRight => VerticalAnchor.Top,
        WindowAnchorEdge.BottomLeft or WindowAnchorEdge.BottomCenter or WindowAnchorEdge.BottomRight => VerticalAnchor.Bottom,
        _ => VerticalAnchor.Center
    };

    private static double GetHorizontalCoordinate(double anchorCoordinate, double size, HorizontalAnchor alignment) => alignment switch {
        HorizontalAnchor.Left => anchorCoordinate,
        HorizontalAnchor.Right => anchorCoordinate - size,
        _ => anchorCoordinate - (size * 0.5)
    };

    private static double GetVerticalCoordinate(double anchorCoordinate, double size, VerticalAnchor alignment) => alignment switch {
        VerticalAnchor.Top => anchorCoordinate,
        VerticalAnchor.Bottom => anchorCoordinate - size,
        _ => anchorCoordinate - (size * 0.5)
    };

    private static TaskbarEdge DetectTaskbarEdge(MonitorAreas areas) {
        var leftInset = areas.WorkArea.Left - areas.MonitorArea.Left;
        var topInset = areas.WorkArea.Top - areas.MonitorArea.Top;
        var rightInset = areas.MonitorArea.Right - areas.WorkArea.Right;
        var bottomInset = areas.MonitorArea.Bottom - areas.WorkArea.Bottom;

        var maxInset = Math.Max(Math.Max(leftInset, rightInset), Math.Max(topInset, bottomInset));

        if (maxInset <= 0) {
            return TaskbarEdge.Bottom;
        }

        if (Math.Abs(maxInset - leftInset) < 0.5) {
            return TaskbarEdge.Left;
        }

        if (Math.Abs(maxInset - topInset) < 0.5) {
            return TaskbarEdge.Top;
        }

        if (Math.Abs(maxInset - rightInset) < 0.5) {
            return TaskbarEdge.Right;
        }

        return TaskbarEdge.Bottom;
    }

    private MonitorAreas GetCurrentMonitorAreas() {
        var point = GetWindowCenter();
        return GetMonitorAreasForPoint(point);
    }

    private static MonitorAreas GetMonitorAreasForPoint(Point point) {
        var monitor = MonitorFromPoint(new NativePoint(point), MonitorDefaultToNearest);
        return TryGetMonitorAreas(monitor, out var areas)
            ? areas
            : new MonitorAreas(SystemParameters.WorkArea, SystemParameters.WorkArea);
    }

    private static bool TryGetMonitorAreas(nint monitor, out MonitorAreas areas) {
        if (monitor != nint.Zero) {
            var info = new MonitorInfo {
                cbSize = Marshal.SizeOf<MonitorInfo>()
            };

            if (GetMonitorInfo(monitor, ref info)) {
                areas = new MonitorAreas(info.rcMonitor.ToRect(), info.rcWork.ToRect());
                return true;
            }
        }

        areas = default;
        return false;
    }

    private enum HorizontalAnchor {
        Left,
        Center,
        Right
    }

    private enum VerticalAnchor {
        Top,
        Center,
        Bottom
    }

}
