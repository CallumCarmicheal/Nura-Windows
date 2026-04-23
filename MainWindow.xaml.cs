using System.ComponentModel;
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

    private MainViewModel? _viewModel;
    private bool _isApplyingPlacement;
    private bool _hasManualAnchorOverride;
    private Point _manualAnchorCenter;

    private enum TaskbarEdge {
        Left,
        Top,
        Right,
        Bottom
    }

    public MainWindow() {
        InitializeComponent();
        DataContext = new MainViewModel();

        Loaded += OnLoaded;
        DataContextChanged += OnWindowDataContextChanged;
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

        if (e.PropertyName == nameof(MainViewModel.SelectedWindowAnchorModeValue)) {
            _hasManualAnchorOverride = false;

            Dispatcher.BeginInvoke(
                () => ApplyWindowPlacement(),
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

    private void BeginWindowDrag() {
        var startingLeft = Left;
        var startingTop = Top;

        DragMove();
        HandleManualDragCompleted(startingLeft, startingTop);
    }

    private void HandleManualDragCompleted(double startingLeft, double startingTop) {
        if (_viewModel is null || _isApplyingPlacement) {
            return;
        }

        var moved = Math.Abs(Left - startingLeft) > 0.5 || Math.Abs(Top - startingTop) > 0.5;
        if (!moved) {
            return;
        }

        if (_viewModel.SelectedWindowAnchorModeValue == WindowAnchorMode.RememberLastPosition) {
            _viewModel.SaveRememberedWindowPosition(Left, Top);
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
            var workArea = SystemParameters.WorkArea;
            var targetSize = GetTargetWindowSize();
            var targetRect = GetTargetWindowRect(targetSize, workArea);
            Left = targetRect.Left;
            Top = targetRect.Top;
            Width = targetRect.Width;
            Height = targetRect.Height;

            if (_viewModel.SelectedWindowAnchorModeValue == WindowAnchorMode.RememberLastPosition) {
                _viewModel.SaveRememberedWindowPosition(Left, Top);
            }
        } finally {
            _isApplyingPlacement = false;
        }
    }

    private void AnimateWindowForCurrentState() {
        if (!IsLoaded || _viewModel is null || _isApplyingPlacement) {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var targetSize = GetTargetWindowSize();
        var targetRect = GetTargetWindowRect(targetSize, workArea);
        var duration = TimeSpan.FromMilliseconds(_viewModel.IsExpanded ? 280 : 250);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(WidthProperty, new DoubleAnimation(targetRect.Width, duration) { EasingFunction = ease });
        BeginAnimation(HeightProperty, new DoubleAnimation(targetRect.Height, duration) { EasingFunction = ease });
        BeginAnimation(LeftProperty, new DoubleAnimation(targetRect.Left, duration) { EasingFunction = ease });
        BeginAnimation(TopProperty, new DoubleAnimation(targetRect.Top, duration) { EasingFunction = ease });

        if (_viewModel.SelectedWindowAnchorModeValue == WindowAnchorMode.RememberLastPosition) {
            _viewModel.SaveRememberedWindowPosition(targetRect.Left, targetRect.Top);
        }
    }

    private Rect GetTargetWindowRect(Size targetSize, Rect workArea) {
        if (_viewModel is null) {
            return new Rect(Left, Top, targetSize.Width, targetSize.Height);
        }

        if (_hasManualAnchorOverride && _viewModel.SelectedWindowAnchorModeValue != WindowAnchorMode.RememberLastPosition) {
            return BuildCenteredRect(_manualAnchorCenter, targetSize, workArea);
        }

        return _viewModel.SelectedWindowAnchorModeValue switch {
            WindowAnchorMode.Taskbar => BuildTaskbarRect(targetSize, workArea),
            WindowAnchorMode.RememberLastPosition => BuildRememberedRect(targetSize, workArea),
            WindowAnchorMode.Center => BuildCenteredRect(GetWorkAreaCenter(workArea), targetSize, workArea),
            _ => BuildCenteredRect(GetWorkAreaCenter(workArea), targetSize, workArea)
        };
    }

    private Rect BuildTaskbarRect(Size targetSize, Rect workArea) {
        var edge = DetectTaskbarEdge(workArea);

        double targetLeft;
        double targetTop;

        switch (edge) {
            case TaskbarEdge.Left:
                targetLeft = workArea.Left + WindowMargin;
                targetTop = workArea.Bottom - targetSize.Height - WindowMargin;
                break;

            case TaskbarEdge.Top:
                targetLeft = workArea.Right - targetSize.Width - WindowMargin;
                targetTop = workArea.Top + WindowMargin;
                break;

            case TaskbarEdge.Right:
                targetLeft = workArea.Right - targetSize.Width - WindowMargin;
                targetTop = workArea.Bottom - targetSize.Height - WindowMargin;
                break;

            default:
                targetLeft = workArea.Right - targetSize.Width - WindowMargin;
                targetTop = workArea.Bottom - targetSize.Height - WindowMargin;
                break;
        }

        return ClampRectToWorkArea(new Rect(targetLeft, targetTop, targetSize.Width, targetSize.Height), workArea);
    }

    private Rect BuildRememberedRect(Size targetSize, Rect workArea) {
        if (_viewModel is not null && _viewModel.TryGetRememberedWindowPosition(out var position)) {
            return ClampRectToWorkArea(new Rect(position.X, position.Y, targetSize.Width, targetSize.Height), workArea);
        }

        return BuildCenteredRect(GetWorkAreaCenter(workArea), targetSize, workArea);
    }

    private static Rect BuildCenteredRect(Point center, Size targetSize, Rect workArea) {
        var rect = new Rect(
            center.X - (targetSize.Width * 0.5),
            center.Y - (targetSize.Height * 0.5),
            targetSize.Width,
            targetSize.Height);

        return ClampRectToWorkArea(rect, workArea);
    }

    private static Rect ClampRectToWorkArea(Rect rect, Rect workArea) {
        var clampedLeft = ClampToWorkArea(rect.Left, workArea.Left, workArea.Right - rect.Width);
        var clampedTop = ClampToWorkArea(rect.Top, workArea.Top, workArea.Bottom - rect.Height);
        return new Rect(clampedLeft, clampedTop, rect.Width, rect.Height);
    }

    private static double ClampToWorkArea(double value, double min, double max) {
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

    private Size GetTargetWindowSize() {
        var shellWidth = _viewModel?.IsExpanded == true ? ExpandedShellWidth : CollapsedShellWidth;
        var shellHeight = _viewModel?.IsExpanded == true ? ExpandedShellHeight : CollapsedShellHeight;
        return new Size(shellWidth + OuterMarginSize, shellHeight + OuterMarginSize);
    }

    private static TaskbarEdge DetectTaskbarEdge(Rect workArea) {
        var leftInset = workArea.Left;
        var topInset = workArea.Top;
        var rightInset = SystemParameters.PrimaryScreenWidth - workArea.Right;
        var bottomInset = SystemParameters.PrimaryScreenHeight - workArea.Bottom;

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
}
