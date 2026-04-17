using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NuraPopupWpf.Models;
using NuraPopupWpf.Services;

namespace NuraPopupWpf.Controls;




public sealed class ProfileVisualControl : FrameworkElement {
    private static readonly NuraProfileRenderer BitmapRenderer = new();

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

    private CachedBitmapState? cachedBitmapState = null;

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

        var normalizedCurve = BuildBlendedCurve(fromProfile, toProfile, Clamp(ProfileBlendProgress, 0.0, 1.0));
        var modeProgress = Clamp(ModeProgress, 0.0, 1.0);
        var (col1, col2) = GetProfileColours(Lerp(
            Lerp(0.7, fromProfile.Colour, modeProgress),
            Lerp(0.7, toProfile.Colour, modeProgress),
            Clamp(ProfileBlendProgress, 0.0, 1.0)));

        var bandCount = 6;
        var segments = 144;
        // Match the bitmap renderer's normalized radius math:
        // baseRadius 1.15 and profileScale 0.32..0.58 map to size / 4 in screen space.
        var baseRadius = size * 0.284;
        var profileScale = Lerp(0.0, Lerp(size * 0.078, size * 0.139, (ImmersionValue + 2.0) / 6.0), modeProgress);
        var baseOpacity = Lerp(0.34, 0.48, modeProgress);
        var alphaSink = 0.13;
        var edgeGlowThickness = size * 0.0085;
        var edgeGlowOpacity = Lerp(0.012, 0.020, modeProgress);
        var fieldColour = LerpRgb(col1, col2, 0.5);

        var shadowBrush = new RadialGradientBrush {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.54));
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(34.0, 52.0, modeProgress)), 0, 0, 0), 0.79));
        shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
        shadowBrush.Freeze();
        drawingContext.DrawEllipse(shadowBrush, null, center, size * 0.41, size * 0.41);

        // The bitmap renderer builds brightness from a soft alpha field around the ring,
        // not only from the filled bands. Recreate that ambient energy here.
        var ambientFieldBrush = new RadialGradientBrush {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        ambientFieldBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.0));
        ambientFieldBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.58));
        ambientFieldBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(8.0, 14.0, modeProgress)), (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.72));
        ambientFieldBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(4.0, 8.0, modeProgress)), (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.84));
        ambientFieldBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 1.0));
        ambientFieldBrush.Freeze();
        drawingContext.DrawEllipse(ambientFieldBrush, null, center, size * 0.39, size * 0.39);

        var innerBloomBrush = new RadialGradientBrush {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        innerBloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(3.0, 6.0, modeProgress)), (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.0));
        innerBloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(1.0, 3.0, modeProgress)), (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.38));
        innerBloomBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, (byte)Math.Round(fieldColour.R), (byte)Math.Round(fieldColour.G), (byte)Math.Round(fieldColour.B)), 0.78));
        innerBloomBrush.Freeze();
        drawingContext.DrawEllipse(innerBloomBrush, null, center, size * 0.32, size * 0.32);

        for (var band = 0; band < bandCount; band++) {
            // Match the bitmap renderer exactly: 6 bands at 0/6 .. 5/6, not 0..1 inclusive.
            var fi = band / (double)bandCount;
            var brushColor = LerpRgb(col1, col2, fi);
            var geometry = BuildBandGeometry(center, normalizedCurve, baseRadius, profileScale, fi, segments);
            geometry.Freeze();



            var fill = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(Clamp(baseOpacity - (fi * alphaSink), 0.0, 1.0) * 255.0),
                (byte)Math.Round(brushColor.R),
                (byte)Math.Round(brushColor.G),
                (byte)Math.Round(brushColor.B)));
            fill.Freeze();

            var softFill = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(Clamp((baseOpacity * 0.09) - (fi * (alphaSink * 0.05)), 0.0, 1.0) * 255.0),
                (byte)Math.Round(brushColor.R),
                (byte)Math.Round(brushColor.G),
                (byte)Math.Round(brushColor.B)));
            softFill.Freeze();

            var edgeBrush = new SolidColorBrush(Color.FromArgb(
                (byte)Math.Round(Clamp(edgeGlowOpacity - (fi * 0.003), 0.0, 1.0) * 255.0),
                (byte)Math.Round(brushColor.R),
                (byte)Math.Round(brushColor.G),
                (byte)Math.Round(brushColor.B)));
            edgeBrush.Freeze();

            var edgePen = new Pen(edgeBrush, edgeGlowThickness) {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            edgePen.Freeze();

            drawingContext.DrawGeometry(fill, null, geometry);
            drawingContext.DrawGeometry(softFill, null, geometry);
            drawingContext.DrawGeometry(null, edgePen, geometry);
        }

        var outerGlowBrush = new RadialGradientBrush {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        outerGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(4.0, 8.0, modeProgress)), (byte)Math.Round(col1.R), (byte)Math.Round(col1.G), (byte)Math.Round(col1.B)), 0.0));
        outerGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(Lerp(2.0, 4.0, modeProgress)), (byte)Math.Round(Lerp(col1.R, col2.R, 0.5)), (byte)Math.Round(Lerp(col1.G, col2.G, 0.5)), (byte)Math.Round(Lerp(col1.B, col2.B, 0.5))), 0.55));
        outerGlowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, (byte)Math.Round(col2.R), (byte)Math.Round(col2.G), (byte)Math.Round(col2.B)), 1.0));
        outerGlowBrush.Freeze();
        drawingContext.DrawEllipse(outerGlowBrush, null, center, size * 0.39, size * 0.39);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateBitmapCache();
    }

    private BitmapSource GetOrCreateBitmap(ProfileModel fromProfile, ProfileModel toProfile, double size) {
        var renderSize = Math.Max(1, (int)Math.Round(size));
        var blend = Clamp(ProfileBlendProgress, 0.0, 1.0);
        var mode = Clamp(ModeProgress, 0.0, 1.0);
        var immersion = ImmersionValue;

        // Check if we have already cached the bitmap
        if (cachedBitmapState is not null 
                && cachedBitmapState.Bitmap is not null
                && cachedBitmapState.BitmapSize == renderSize
                && ReferenceEquals(cachedBitmapState.FromProfile, fromProfile)
                && ReferenceEquals(cachedBitmapState.ToProfile, toProfile)
                && NearlyEqual(cachedBitmapState.BlendProgress, blend)
                && NearlyEqual(cachedBitmapState.ModeProgress, mode)
                && NearlyEqual(cachedBitmapState.ImmersionValue, immersion)
                && cachedBitmapState.UseBitmapRenderer == UseBitmapRenderer) {
            return cachedBitmapState.Bitmap;
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
        cachedBitmapState = new CachedBitmapState(
            Bitmap: bitmap, BitmapSize: renderSize,
            FromProfile: fromProfile, ToProfile: toProfile,
            BlendProgress: blend, ModeProgress: mode,
            ImmersionValue: immersion,
            UseBitmapRenderer: UseBitmapRenderer
        );
        
        return bitmap;
    }

    private void InvalidateBitmapCache() {
        cachedBitmapState = new(null, 0, null, null, double.NaN, double.NaN, double.NaN, UseBitmapRenderer);
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.0001;

    private static void OnVisualInputChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) {
        if (dependencyObject is not ProfileVisualControl control) {
            return;
        }

        control.InvalidateBitmapCache();
    }

    private static StreamGeometry BuildBandGeometry(
        Point center,
        IReadOnlyList<double> curve,
        double baseRadius,
        double profileScale,
        double bandFactor,
        int segments) {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        for (var i = 0; i <= segments; i++) {
            var progress = i / (double)segments;
            var theta = progress * Math.PI * 2.0;
            var profileValue = SampleWrappedCubic(curve, progress);
            var radius = baseRadius + (profileValue * profileScale * bandFactor);
            var point = new Point(
                center.X + (Math.Sin(theta) * radius),
                center.Y + (Math.Cos(theta) * radius));

            if (i == 0) {
                context.BeginFigure(point, isFilled: true, isClosed: true);
            } else {
                context.LineTo(point, isStroked: true, isSmoothJoin: true);
            }
        }

        return geometry;
    }

    private static double[] BuildBlendedCurve(ProfileModel fromProfile, ProfileModel toProfile, double blend) {
        var source = BuildProfileCurve(fromProfile.LeftData, fromProfile.RightData);
        var target = BuildProfileCurve(toProfile.LeftData, toProfile.RightData);
        var values = new double[target.Length];

        for (var i = 0; i < target.Length; i++) {
            values[i] = Lerp(source[i], target[i], blend);
        }

        return values;
    }

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

    // Removed because I couldn't quite get the saturation right.
    private static Rgb BoostRgb(Rgb source, double saturationBoost, double brightnessBoost) {
        var average = (source.R + source.G + source.B) / 3.0;
        var saturated = new Rgb(
            average + ((source.R - average) * saturationBoost),
            average + ((source.G - average) * saturationBoost),
            average + ((source.B - average) * saturationBoost));

        return new Rgb(
            Clamp(saturated.R * brightnessBoost, 0.0, 255.0),
            Clamp(saturated.G * brightnessBoost, 0.0, 255.0),
            Clamp(saturated.B * brightnessBoost, 0.0, 255.0));
    }

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
