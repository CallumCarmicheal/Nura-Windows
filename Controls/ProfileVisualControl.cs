using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

using NuraPopupWpf.Models;
using NuraPopupWpf.Services;

namespace NuraPopupWpf.Controls;

/// <summary>
/// Render the hearing profile morphing animation using either a cached bitmap or a retained-mode vector drawing, depending on the <see cref="UseBitmapRenderer"/> property.
/// </summary>
public sealed class ProfileVisualControl : FrameworkElement {
    private static readonly NuraProfileRenderer BitmapRenderer = new();
    private const int BAND_COUNT = 6;
    private const int SHAPE_SEGMENTS = 120;

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

    public static readonly DependencyProperty ImmersionValueProperty =
        DependencyProperty.Register(
            nameof(ImmersionValue),
            typeof(double),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty UseBitmapRendererProperty =
        DependencyProperty.Register(
            nameof(UseBitmapRenderer),
            typeof(bool),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty IsMorphingProperty =
        DependencyProperty.Register(
            nameof(IsMorphing),
            typeof(bool),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    public static readonly DependencyProperty RenderShadowProperty =
        DependencyProperty.Register(
            nameof(RenderShadow),
            typeof(bool),
            typeof(ProfileVisualControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnVisualInputChanged));

    /// <summary>
    /// Gradient colours for the hearing profile.
    /// </summary>
    private static readonly GradientStopModel[] GradientStops = {
        new(0.00, 240, 127, 87),
        new(0.12, 236, 28, 36),
        new(0.28, 238, 88, 149),
        new(0.41, 160, 76, 156),
        new(0.60, 61, 83, 163),
        new(0.80, 8, 152, 205),
        new(1.00, 141, 209, 199),
    };

    private sealed record CachedBitmapState(
        BitmapSource? Bitmap,
        double BitmapSize,
        ProfileModel? FromProfile,
        ProfileModel? ToProfile,
        double BlendProgress,
        double ModeProgress,
        double ImmersionValue,
        bool UseBitmapRenderer);

    private static readonly ConditionalWeakTable<ProfileModel, double[]> ProfileCurveCache = new();
    private CachedBitmapState? bitmapState = null;
    private RetainedShapeState? shapeState = null;
    private DrawingGroup? shapeDrawing = null;
    private RetainedBandLayer[] bandLayers = Array.Empty<RetainedBandLayer>();
    private EllipseGeometry? shadowGeometry; // TODO: Turn these into a record.
    private EllipseGeometry? ambientFieldGeometry;
    private EllipseGeometry? innerBloomGeometry;
    private EllipseGeometry? outerGlowGeometry;
    private RadialGradientBrush? shadowBrush;
    private RadialGradientBrush? ambientFieldBrush;
    private RadialGradientBrush? innerBloomBrush;
    private RadialGradientBrush? outerGlowBrush;
    private double[]? blendedCurveBuffer;
    private double shapeSize;

    private sealed record RetainedShapeState(
        double DrawingSize,
        ProfileModel? FromProfile,
        ProfileModel? ToProfile,
        double BlendProgress,
        double ModeProgress,
        double ImmersionValue,
        bool IsMorphing,
        bool RenderShadow);

    private sealed class RetainedBandLayer {
        public required PathFigure Figure { get; init; }
        public required PolyLineSegment Segment { get; init; }
        public required SolidColorBrush FillBrush { get; init; }
        public required SolidColorBrush SoftFillBrush { get; init; }
        public required SolidColorBrush EdgeBrush { get; init; }
        public required Pen EdgePen { get; init; }
    }

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

    public double ImmersionValue {
        get => (double)GetValue(ImmersionValueProperty);
        set => SetValue(ImmersionValueProperty, value);
    }

    public bool UseBitmapRenderer {
        get => (bool)GetValue(UseBitmapRendererProperty);
        set => SetValue(UseBitmapRendererProperty, value);
    }

    public bool IsMorphing {
        get => (bool)GetValue(IsMorphingProperty);
        set => SetValue(IsMorphingProperty, value);
    }

    public bool RenderShadow {
        get => (bool)GetValue(RenderShadowProperty);
        set => SetValue(RenderShadowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) {
        var side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 240 : availableSize.Height);

        if (double.IsNaN(side) || side <= 0) {
            side = 240;
        }

        return new Size(side, side);
    }


    protected override void OnRender(DrawingContext drawingContext) {
        base.OnRender(drawingContext);

        var fromProfile = FromProfile ?? ToProfile;
        var toProfile = ToProfile ?? FromProfile;
        if (fromProfile is null || toProfile is null) 
            return;

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        var center = new Point(width * 0.5, height * 0.5);
        var size = Math.Min(width, height);

        // Check if we are using the bitmap renderer.
        if (UseBitmapRenderer) {
            var bitmap = GetOrCreateBitmap(fromProfile, toProfile, size);
            drawingContext.DrawImage(bitmap, new Rect(center.X - (size * 0.5), center.Y - (size * 0.5), size, size));
            return;
        }

        EnsureShapeResources(size);
        CalculateShapeGeometry(fromProfile, toProfile, size);

        if (shapeDrawing is null) {
            return;
        }

        drawingContext.PushTransform(new TranslateTransform(center.X - (size * 0.5), center.Y - (size * 0.5)));
        drawingContext.DrawDrawing(shapeDrawing);
        drawingContext.Pop();
    }

    private void EnsureShapeResources(double size) {
        var drawingSize = Math.Max(1, Math.Round(size));

        // Check if we already have a drawing group, if so no need to recreate
        //   the resources unless the size has changed.
        if (shapeDrawing is not null && NearlyEqual(shapeSize, drawingSize)) {
            return;
        }

        shapeSize = drawingSize;
        shapeState = null;
        shapeDrawing = new DrawingGroup();
        bandLayers = new RetainedBandLayer[BAND_COUNT];

        shadowGeometry = new EllipseGeometry();
        ambientFieldGeometry = new EllipseGeometry();
        innerBloomGeometry = new EllipseGeometry();
        outerGlowGeometry = new EllipseGeometry();

        shadowBrush = CreateMutableRadialBrush(new[] { 0.0, 0.60, 0.70, 0.82, 0.92, 0.98, 1.0 });
        ambientFieldBrush = CreateMutableRadialBrush(new[] { 0.0, 0.58, 0.72, 0.84, 1.0 });
        innerBloomBrush = CreateMutableRadialBrush(new[] { 0.0, 0.38, 0.78 });
        outerGlowBrush = CreateMutableRadialBrush(new[] { 0.0, 0.55, 1.0 });

        shapeDrawing.Children.Add(new GeometryDrawing(shadowBrush, null, shadowGeometry));
        shapeDrawing.Children.Add(new GeometryDrawing(ambientFieldBrush, null, ambientFieldGeometry));
        shapeDrawing.Children.Add(new GeometryDrawing(innerBloomBrush, null, innerBloomGeometry));

        for (var band = 0; band < BAND_COUNT; band++) {
            var figure = new PathFigure { IsClosed = true, IsFilled = true };
            var segment = new PolyLineSegment();
            figure.Segments.Add(segment);
            var geometry = new PathGeometry(new[] { figure });

            var fillBrush = new SolidColorBrush(Colors.Transparent);
            var softFillBrush = new SolidColorBrush(Colors.Transparent);
            var edgeBrush = new SolidColorBrush(Colors.Transparent);
            var edgePen = new Pen(edgeBrush, 0.0) {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            shapeDrawing.Children.Add(new GeometryDrawing(fillBrush, null, geometry));
            shapeDrawing.Children.Add(new GeometryDrawing(softFillBrush, null, geometry));
            shapeDrawing.Children.Add(new GeometryDrawing(null, edgePen, geometry));

            bandLayers[band] = new RetainedBandLayer {
                Figure = figure,
                Segment = segment,
                FillBrush = fillBrush,
                SoftFillBrush = softFillBrush,
                EdgeBrush = edgeBrush,
                EdgePen = edgePen
            };
        }

        shapeDrawing.Children.Add(new GeometryDrawing(outerGlowBrush, null, outerGlowGeometry));
    }

    /// <summary>
    /// Update the shape geometry and brushes based on the current profiles, 
    /// blend progress, mode progress, and immersion value. 
    /// 
    /// This is the most expensive part of the rendering process, 
    /// so we cache the results and only recalculate when necessary.
    /// </summary>
    /// <param name="fromProfile">Originating profile</param>
    /// <param name="toProfile">Target profile</param>
    /// <param name="size">Size of the shape (Width / Height, square)</param>
    private void CalculateShapeGeometry(ProfileModel fromProfile, ProfileModel toProfile, double size) {
        if (shapeDrawing is null
            || shadowGeometry is null
            || ambientFieldGeometry is null
            || innerBloomGeometry is null
            || outerGlowGeometry is null
            || shadowBrush is null
            || ambientFieldBrush is null
            || innerBloomBrush is null
            || outerGlowBrush is null) {
            return;
        }

        var drawingSize = Math.Max(1, Math.Round(size));
        var blend = Clamp(ProfileBlendProgress, 0.0, 1.0);
        var modeProgress = Clamp(ModeProgress, 0.0, 1.0);
        var immersion = ImmersionValue;
        var isMorphing = IsMorphing;

        if (shapeState is not null
            && NearlyEqual(shapeState.DrawingSize, drawingSize)
            && ReferenceEquals(shapeState.FromProfile, fromProfile)
            && ReferenceEquals(shapeState.ToProfile, toProfile)
            && NearlyEqual(shapeState.BlendProgress, blend)
            && NearlyEqual(shapeState.ModeProgress, modeProgress)
            && NearlyEqual(shapeState.ImmersionValue, immersion)
            && shapeState.IsMorphing == isMorphing
            && shapeState.RenderShadow == RenderShadow) {
            return;
        }

        // Update the retained state to reuse later.
        shapeState = new RetainedShapeState(drawingSize, fromProfile, toProfile, blend, modeProgress, immersion, isMorphing, RenderShadow);

        var center = new Point(drawingSize * 0.5, drawingSize * 0.5);
        var curve = BuildBlendedCurve(fromProfile, toProfile, blend);
        var (col1, col2) = GetProfileColours(Lerp( a: Lerp(0.7, fromProfile.Colour, modeProgress),
                                                   b: Lerp(0.7, toProfile.Colour, modeProgress),
                                                   t: blend));

        var baseRadius = drawingSize * 0.284;
        var profileScale = Lerp(0.0, Lerp(drawingSize * 0.078, drawingSize * 0.139, (immersion + 2.0) / 6.0), modeProgress);
        var baseOpacity = Lerp(0.34, 0.48, modeProgress);
        var alphaSink = 0.13;
        var edgeGlowThickness = drawingSize * (isMorphing ? 0.006 : 0.0085);
        var edgeGlowOpacity = Lerp(isMorphing ? 0.008 : 0.012, isMorphing ? 0.014 : 0.020, modeProgress);
        var fieldColour = LerpRgb(col1, col2, 0.5);

        // Render the shadow first
        UpdateEllipse(shadowGeometry, center, drawingSize * 0.394, drawingSize * 0.394);
        UpdateMutableRadialBrush(
            shadowBrush,
            RenderShadow
                ? [
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb((byte)Math.Round(Lerp(10.0, 16.0, modeProgress)), 0, 0, 0),
                    Color.FromArgb((byte)Math.Round(Lerp(6.0, 10.0, modeProgress)), 0, 0, 0),
                    Color.FromArgb((byte)Math.Round(Lerp(3.0, 6.0, modeProgress)), 0, 0, 0),
                    Color.FromArgb((byte)Math.Round(Lerp(1.0, 2.0, modeProgress)), 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0)
                ]
                : [
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0),
                    Color.FromArgb(0, 0, 0, 0)
                ]);

        // Render the ambient field (rings)
        UpdateEllipse(ambientFieldGeometry, center, drawingSize * 0.39, drawingSize * 0.39);
        UpdateMutableRadialBrush(
            ambientFieldBrush,
            [
                Color.FromArgb(0, ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B)),
                Color.FromArgb(0, ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B)),
                Color.FromArgb((byte)Math.Round(Lerp(8.0, 14.0, modeProgress)), ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B)),
                Color.FromArgb((byte)Math.Round(Lerp(4.0, 8.0, modeProgress)), ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B)),
                Color.FromArgb(0, ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B))
            ]);

        UpdateEllipse(innerBloomGeometry, center, drawingSize * 0.32, drawingSize * 0.32);
        UpdateMutableRadialBrush(
            innerBloomBrush,
            [
                Color.FromArgb((byte)Math.Round(Lerp(3.0, 6.0, modeProgress)), ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B)),
                Color.FromArgb((byte)Math.Round(Lerp(1.0, 3.0, modeProgress)), ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B)),
                Color.FromArgb(0, ToByte(fieldColour.R), ToByte(fieldColour.G), ToByte(fieldColour.B))
            ]);

        UpdateEllipse(outerGlowGeometry, center, drawingSize * 0.39, drawingSize * 0.39);
        UpdateMutableRadialBrush(
            outerGlowBrush,
            [
                Color.FromArgb((byte)Math.Round(Lerp(4.0, 8.0, modeProgress)), ToByte(col1.R), ToByte(col1.G), ToByte(col1.B)),
                Color.FromArgb((byte)Math.Round(Lerp(2.0, 4.0, modeProgress)), ToByte(Lerp(col1.R, col2.R, 0.5)), ToByte(Lerp(col1.G, col2.G, 0.5)), ToByte(Lerp(col1.B, col2.B, 0.5))),
                Color.FromArgb(0, ToByte(col2.R), ToByte(col2.G), ToByte(col2.B))
            ]);

        // Render the hearing bands (layered)
        for (var band = 0; band < BAND_COUNT; band++) {
            var fi = band / (double)BAND_COUNT;
            var brushColor = LerpRgb(col1, col2, fi);
            var layer = bandLayers[band];

            layer.FillBrush.Color = Color.FromArgb(
                (byte)Math.Round(Clamp(baseOpacity - (fi * alphaSink), 0.0, 1.0) * 255.0),
                ToByte(brushColor.R),
                ToByte(brushColor.G),
                ToByte(brushColor.B));

            layer.SoftFillBrush.Color = isMorphing
                ? Colors.Transparent
                : Color.FromArgb(
                    (byte)Math.Round(Clamp((baseOpacity * 0.09) - (fi * (alphaSink * 0.05)), 0.0, 1.0) * 255.0),
                    ToByte(brushColor.R),
                    ToByte(brushColor.G),
                    ToByte(brushColor.B));

            layer.EdgeBrush.Color = Color.FromArgb(
                (byte)Math.Round(Clamp(edgeGlowOpacity - (fi * 0.003), 0.0, 1.0) * 255.0),
                ToByte(brushColor.R),
                ToByte(brushColor.G),
                ToByte(brushColor.B));
            layer.EdgePen.Thickness = edgeGlowThickness;

            UpdateBandGeometry(layer, center, curve, baseRadius, profileScale, fi);
        }

        return;
    }

    private void UpdateBandGeometry(
        RetainedBandLayer layer,
        Point center,
        IReadOnlyList<double> curve,
        double baseRadius,
        double profileScale,
        double bandFactor
    ) {
        EnsurePointCollectionSize(layer.Segment.Points, SHAPE_SEGMENTS);

        for (var i = 0; i <= SHAPE_SEGMENTS; i++) {
            var progress = i / (double)SHAPE_SEGMENTS;
            var theta = progress * Math.PI * 2.0;
            var profileValue = SampleWrappedCubic(curve, progress);
            var radius = baseRadius + (profileValue * profileScale * bandFactor);
            var point = new Point(
                center.X + (Math.Sin(theta) * radius),
                center.Y + (Math.Cos(theta) * radius));

            if (i == 0) {
                layer.Figure.StartPoint = point;
            } else {
                layer.Segment.Points[i - 1] = point;
            }
        }
    }

    private void ResetShapeResources() {
        shapeState = null;
        shapeDrawing = null;
        bandLayers = Array.Empty<RetainedBandLayer>();
        shadowGeometry = null;
        ambientFieldGeometry = null;
        innerBloomGeometry = null;
        outerGlowGeometry = null;
        shadowBrush = null;
        ambientFieldBrush = null;
        innerBloomBrush = null;
        outerGlowBrush = null;
        shapeSize = 0;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateRenderCaches();
        ResetShapeResources();
    }

    private BitmapSource GetOrCreateBitmap(ProfileModel fromProfile, ProfileModel toProfile, double size) {
        var renderSize = Math.Max(1, (int)Math.Round(size));
        var blend = Clamp(ProfileBlendProgress, 0.0, 1.0);
        var mode = Clamp(ModeProgress, 0.0, 1.0);
        var immersion = ImmersionValue;

        // Check if we have already cached the bitmap
        if (bitmapState is not null 
                && bitmapState.Bitmap is not null
                && bitmapState.BitmapSize == renderSize
                && ReferenceEquals(bitmapState.FromProfile, fromProfile)
                && ReferenceEquals(bitmapState.ToProfile, toProfile)
                && NearlyEqual(bitmapState.BlendProgress, blend)
                && NearlyEqual(bitmapState.ModeProgress, mode)
                && NearlyEqual(bitmapState.ImmersionValue, immersion)
                && bitmapState.UseBitmapRenderer == UseBitmapRenderer) {
            return bitmapState.Bitmap;
        }

        // Render the hearing profile onto a bitmap.
        var bitmap = BitmapRenderer.Render(
                targetProfile: toProfile,
                fromProfile: fromProfile,
                profileBlendProgress: blend,
                personalisationProgress: mode,
                size: renderSize,
                immersionValue: immersion);

        // Cache our new bitmap state.
        bitmapState = new CachedBitmapState(
            Bitmap: bitmap, BitmapSize: renderSize,
            FromProfile: fromProfile, ToProfile: toProfile,
            BlendProgress: blend, ModeProgress: mode,
            ImmersionValue: immersion,
            UseBitmapRenderer: UseBitmapRenderer
        );
        
        return bitmap;
    }

    private void InvalidateBitmapCache() {
        bitmapState = new(null, 0, null, null, double.NaN, double.NaN, double.NaN, UseBitmapRenderer);
    }

    private void InvalidateRenderCaches() {
        InvalidateBitmapCache();
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.0001;

    private static void OnVisualInputChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) {
        if (dependencyObject is not ProfileVisualControl control) {
            return;
        }

        control.InvalidateRenderCaches();
        control.shapeState = null;
        control.InvalidateVisual();
    }

    private double[] BuildBlendedCurve(ProfileModel fromProfile, ProfileModel toProfile, double blend) {
        var source = GetOrCreateProfileCurve(fromProfile);
        var target = GetOrCreateProfileCurve(toProfile);

        blendedCurveBuffer ??= new double[target.Length];
        if (blendedCurveBuffer.Length != target.Length) {
            blendedCurveBuffer = new double[target.Length];
        }

        for (var i = 0; i < target.Length; i++) {
            blendedCurveBuffer[i] = Lerp(source[i], target[i], blend);
        }

        return blendedCurveBuffer;
    }

    private static double[] GetOrCreateProfileCurve(ProfileModel profile) =>
        ProfileCurveCache.GetValue(profile, static key => BuildProfileCurve(key.LeftData, key.RightData));

    private static double[] BuildProfileCurve(IReadOnlyList<double> leftData, IReadOnlyList<double> rightData) {
        var left = MakeValues(leftData);
        var right = MakeValues(rightData);
        var count = Math.Min(left.Length, right.Length);
        var values = new double[count];

        for (var i = 0; i < count; i++) {
            values[i] = (left[i] + right[i]) * 0.5;
        }

        return NormalizeCurve(values);
    }

    private static double[] MakeValues(IReadOnlyList<double> raw) {
        var values = new List<double>();
        var first = (raw[0] + raw[1] + raw[2]) / 3.0;
        values.Add(ShapeEdgeValue(first));

        for (var i = 3; i <= raw.Count - 2; i++) {
            values.Add(ShapeValue(raw[i]));
        }

        values.Add(ShapeEdgeValue(raw[^1]));
        return values.ToArray();
    }

    private static double[] NormalizeCurve(IReadOnlyList<double> values) {
        var maxAbs = 0.0;
        foreach (var value in values) {
            maxAbs = Math.Max(maxAbs, Math.Abs(value));
        }

        if (maxAbs < 0.00001) {
            maxAbs = 1.0;
        }

        return values.Select(value => value / maxAbs).ToArray();
    }

    private static double SampleWrappedCubic(IReadOnlyList<double> values, double t) {
        var count = values.Count;
        var x = Fract(t) * count;
        var index = (int)Math.Floor(x);
        var fraction = x - index;

        var p0 = values[Mod(index - 1, count)];
        var p1 = values[Mod(index, count)];
        var p2 = values[Mod(index + 1, count)];
        var p3 = values[Mod(index + 2, count)];

        var f2 = fraction * fraction;
        var f3 = f2 * fraction;

        var value = p1
                    + (0.5 * fraction * (p2 - p0))
                    + (0.5 * f2 * ((2.0 * p0) - (5.0 * p1) + (4.0 * p2) - p3))
                    + (0.5 * f3 * (-p0 + (3.0 * p1) - (3.0 * p2) + p3));

        return Clamp(value, -1.0, 1.0);
    }

    private static (Rgb Col1, Rgb Col2) GetProfileColours(double colour) {
        var baseHue = 0.35;
        var gradientShift = 0.65;
        var c = Clamp(colour, 0.0, 1.0);
        var hue1 = Fract(baseHue + c);
        var hue2 = Fract(hue1 + gradientShift);
        var t1 = Math.Abs(1.0 - (hue1 * 2.0));
        var t2 = Math.Abs(1.0 - (hue2 * 2.0));

        return (SampleGradient(t1), SampleGradient(t2));
    }

    private static Rgb SampleGradient(double t) {
        var x = Clamp(t, 0.0, 1.0);
        for (var i = 0; i < GradientStops.Length - 1; i++) {
            var a = GradientStops[i];
            var b = GradientStops[i + 1];
            if (x >= a.T && x <= b.T) {
                var localT = (x - a.T) / (b.T - a.T);
                return LerpRgb(new Rgb(a.R, a.G, a.B), new Rgb(b.R, b.G, b.B), localT);
            }
        }

        var last = GradientStops[^1];
        return new Rgb(last.R, last.G, last.B);
    }

    private static Rgb LerpRgb(Rgb a, Rgb b, double t) =>
        new(Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));

    private static RadialGradientBrush CreateMutableRadialBrush(double[] offsets) {
        var brush = new RadialGradientBrush {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };

        foreach (var offset in offsets) {
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, offset));
        }

        return brush;
    }

    private static void UpdateMutableRadialBrush(RadialGradientBrush brush, Color[] colors) {
        for (var i = 0; i < colors.Length && i < brush.GradientStops.Count; i++) {
            brush.GradientStops[i].Color = colors[i];
        }
    }

    private static void UpdateEllipse(EllipseGeometry geometry, Point center, double radiusX, double radiusY) {
        geometry.Center = center;
        geometry.RadiusX = radiusX;
        geometry.RadiusY = radiusY;
    }

    private static void EnsurePointCollectionSize(PointCollection points, int count) {
        while (points.Count < count) {
            points.Add(default);
        }

        while (points.Count > count) {
            points.RemoveAt(points.Count - 1);
        }
    }

    private static byte ToByte(double value) => (byte)Math.Round(Clamp(value, 0.0, 255.0));

    private static double ShapeValue(double x) => (Math.Atan(x * 0.3) * 0.4) / (Math.PI / 2.0);

    private static double ShapeEdgeValue(double x) => (Math.Atan(x * 0.15) * 0.4) / (Math.PI / 2.0);

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    private static double Fract(double value) => value - Math.Floor(value);

    private static int Mod(int value, int size) => ((value % size) + size) % size;

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private readonly record struct GradientStopModel(double T, double R, double G, double B);
    private readonly record struct Rgb(double R, double G, double B);
}
