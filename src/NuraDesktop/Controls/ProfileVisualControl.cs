using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NuraLib.Rendering;

using NuraDesktop.Models;

namespace NuraDesktop.Controls;

/// <summary>
/// Renders a hearing profile with either the native-reference bitmap path or retained WPF contours.
/// </summary>
public sealed class ProfileVisualControl : FrameworkElement {
    public static readonly DependencyProperty FromProfileProperty =
        DependencyProperty.Register(
            nameof(FromProfile),
            typeof(ProfileModel),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty ToProfileProperty =
        DependencyProperty.Register(
            nameof(ToProfile),
            typeof(ProfileModel),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty ProfileBlendProgressProperty =
        DependencyProperty.Register(
            nameof(ProfileBlendProgress),
            typeof(double),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty ModeProgressProperty =
        DependencyProperty.Register(
            nameof(ModeProgress),
            typeof(double),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty UseBitmapRendererProperty =
        DependencyProperty.Register(
            nameof(UseBitmapRenderer),
            typeof(bool),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty BackgroundModeProperty =
        DependencyProperty.Register(
            nameof(BackgroundMode),
            typeof(ProfileVisualBackgroundMode),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(ProfileVisualBackgroundMode.Transparent, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    private readonly NuraProfileShapeRenderer _shapeRenderer = new();
    private CachedBitmapState? _bitmapState;

    public ProfileModel? FromProfile {
        get => (ProfileModel?)GetValue(FromProfileProperty);
        set => SetValue(FromProfileProperty, value);
    }

    public ProfileModel? ToProfile {
        get => (ProfileModel?)GetValue(ToProfileProperty);
        set => SetValue(ToProfileProperty, value);
    }

    public double ProfileBlendProgress {
        get => (double)GetValue(ProfileBlendProgressProperty);
        set => SetValue(ProfileBlendProgressProperty, value);
    }

    public double ModeProgress {
        get => (double)GetValue(ModeProgressProperty);
        set => SetValue(ModeProgressProperty, value);
    }

    public bool UseBitmapRenderer {
        get => (bool)GetValue(UseBitmapRendererProperty);
        set => SetValue(UseBitmapRendererProperty, value);
    }

    public ProfileVisualBackgroundMode BackgroundMode {
        get => (ProfileVisualBackgroundMode)GetValue(BackgroundModeProperty);
        set => SetValue(BackgroundModeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) {
        var side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 240 : availableSize.Height);

        return double.IsNaN(side) || side <= 0 ? new Size(240, 240) : new Size(side, side);
    }

    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);

        var fromProfile = FromProfile ?? ToProfile;
        var toProfile = ToProfile ?? FromProfile;
        if (fromProfile is null || toProfile is null || ActualWidth <= 0 || ActualHeight <= 0) {
            return;
        }

        var size = Math.Min(ActualWidth, ActualHeight);
        var origin = new Point((ActualWidth - size) * 0.5, (ActualHeight - size) * 0.5);

        if (UseBitmapRenderer) {
            var bitmap = GetOrCreateBitmap(fromProfile, toProfile, size);
            drawingContext.DrawImage(bitmap, new Rect(origin, new Size(size, size)));
            return;
        }

        var drawing = _shapeRenderer.Render(
            fromProfile.VisualisationData,
            toProfile.VisualisationData,
            ProfileBlendProgress,
            ModeProgress,
            size,
            BackgroundMode);

        drawingContext.PushTransform(new TranslateTransform(origin.X, origin.Y));
        drawingContext.DrawDrawing(drawing);
        drawingContext.Pop();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateRendererCaches();
    }

    private BitmapSource GetOrCreateBitmap(ProfileModel fromProfile, ProfileModel toProfile, double size) {
        var renderSize = Math.Max(1, (int)Math.Round(size));
        var blend = Math.Clamp(ProfileBlendProgress, 0.0, 1.0);
        var mode = Math.Clamp(ModeProgress, 0.0, 1.0);

        if (_bitmapState is not null
            && _bitmapState.Size == renderSize
            && ReferenceEquals(_bitmapState.FromProfile, fromProfile)
            && ReferenceEquals(_bitmapState.ToProfile, toProfile)
            && NearlyEqual(_bitmapState.BlendProgress, blend)
            && NearlyEqual(_bitmapState.ModeProgress, mode)
            && _bitmapState.BackgroundMode == BackgroundMode) {
            return _bitmapState.Bitmap;
        }

        var rawBitmap = NuraProfileBitmapRenderer.Render(
            toProfile.VisualisationData,
            fromProfile.VisualisationData,
            blend,
            mode,
            renderSize,
            useTransparency: BackgroundMode == ProfileVisualBackgroundMode.Transparent);
        var bitmap = rawBitmap.ToBitmapSource();

        _bitmapState = new CachedBitmapState(bitmap, renderSize, fromProfile, toProfile, blend, mode, BackgroundMode);
        return bitmap;
    }

    private void InvalidateRendererCaches() {
        _bitmapState = null;
        _shapeRenderer.Invalidate();
    }

    private static void OnVisualInputChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) {
        if (dependencyObject is ProfileVisualControl control) {
            control.InvalidateRendererCaches();
        }
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.0001;

    private sealed record CachedBitmapState(
        BitmapSource Bitmap,
        int Size,
        ProfileModel FromProfile,
        ProfileModel ToProfile,
        double BlendProgress,
        double ModeProgress,
        ProfileVisualBackgroundMode BackgroundMode);
}
