using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NuraPopupWpf.Models;

namespace NuraPopupWpf.Services;

public sealed class NuraProfileRenderer {
    private static readonly GradientStopModel[] GradientStops =
    {
        new(0.00, 240, 127, 87),
        new(0.12, 236, 28, 36),
        new(0.28, 238, 88, 149),
        new(0.41, 160, 76, 156),
        new(0.60, 61, 83, 163),
        new(0.80, 8, 152, 205),
        new(1.00, 141, 209, 199),
    };

    private const double HalfPi = Math.PI / 2.0;

    public BitmapSource Render(
        ProfileModel targetProfile,
        ProfileModel fromProfile,
        double profileBlendProgress,
        double personalisationProgress,
        int size,
        double immersionValue
    ) {
        var width = size;
        var height = size;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        RenderInto(
            targetProfile,
            fromProfile,
            profileBlendProgress,
            personalisationProgress,
            size,
            immersionValue,
            pixels);

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    public void RenderInto(
        ProfileModel targetProfile,
        ProfileModel fromProfile,
        double profileBlendProgress,
        double personalisationProgress,
        int size,
        double immersionValue,
        byte[] pixels
    ) {
        var width = size;
        var height = size;
        var stride = width * 4;

        if (pixels.Length < stride * height) {
            throw new ArgumentException("Pixel buffer is smaller than the requested render size.", nameof(pixels));
        }

        Array.Clear(pixels, 0, stride * height);

        var targetCurve = BuildProfileCurve(targetProfile.LeftData, targetProfile.RightData);
        var sourceCurve = BuildProfileCurve(fromProfile.LeftData, fromProfile.RightData);
        var blendedCurve = new double[targetCurve.Length];

        for (var i = 0; i < targetCurve.Length; i++) {
            blendedCurve[i] = Lerp(sourceCurve[i], targetCurve[i], profileBlendProgress) * personalisationProgress;
        }

        var fromColour = Lerp(0.7, fromProfile.Colour, personalisationProgress);
        var toColour = Lerp(0.7, targetProfile.Colour, personalisationProgress);
        var transitionColour = Lerp(fromColour, toColour, profileBlendProgress);
        var colours = GetProfileColours(transitionColour);

        var baseRadius = 1.15;
        var profileScale = Lerp(0.0, Lerp(0.32, 0.58, (immersionValue + 2.0) / 6.0), personalisationProgress);
        var edgeSoftness = 0.028;
        var bandCount = 6;
        var baseAlpha = Lerp(0.34, 0.48, personalisationProgress);
        var alphaSink = 0.13;

        for (var y = 0; y < height; y++) {
            for (var x = 0; x < width; x++) {
                var uvX = (x + 0.5) / width;
                var uvY = (y + 0.5) / height;
                var px = (uvX - 0.5) * 2.0;
                var py = (uvY - 0.5) * 2.0;
                var radius = Math.Sqrt((px * px) + (py * py)) * 2.0;
                var angle = Fract(Math.Atan2(px, py) / (Math.PI * 2.0));
                var profileValue = SampleWrappedCubic(blendedCurve, angle);

                var color = new Rgb(0, 0, 0);

                for (var band = 0; band < bandCount; band++) {
                    var fi = band / (double)bandCount;
                    var bandOffset = profileValue * fi * profileScale;
                    var edge = baseRadius + bandOffset;
                    var mask = SmoothStep(edge + edgeSoftness, edge - edgeSoftness, radius);
                    var alpha = Math.Max(0.0, baseAlpha - (alphaSink * fi)) * mask;
                    var bandColor = MixRgb(colours.Col1, colours.Col2, fi);
                    color = AlphaComposite(color, bandColor, alpha);
                }

                var glow = Clamp(1.0 - (Math.Abs(radius - baseRadius) / 0.23), 0.0, 1.0) * 0.06;
                color = AlphaComposite(color, MixRgb(colours.Col1, colours.Col2, 0.5), glow);

                var intensity = Math.Max(color.R, Math.Max(color.G, color.B)) / 255.0;
                var softField = Clamp(1.0 - (Math.Abs(radius - baseRadius) / 0.42), 0.0, 1.0) * 0.18;
                var alphaChannel = Clamp((intensity * 1.2) + softField, 0.0, 1.0);

                var index = (y * stride) + (x * 4);
                pixels[index + 0] = (byte)Math.Round(color.B);
                pixels[index + 1] = (byte)Math.Round(color.G);
                pixels[index + 2] = (byte)Math.Round(color.R);
                pixels[index + 3] = (byte)Math.Round(alphaChannel * 255.0);
            }
        }
    }

    public BitmapSource RenderThumbnail(ProfileModel profile, int size) {
        return Render(profile, profile, profileBlendProgress: 1.0, personalisationProgress: 1.0, size, immersionValue: 1.0);
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
        if (raw.Count < 4) {
            throw new InvalidOperationException("Profile data must contain at least 4 values.");
        }

        var values = new List<double>();
        var first = (raw[0] + raw[1] + raw[2]) / 3.0;
        values.Add(ShapeEdgeValue(first));

        for (var i = 3; i <= raw.Count - 2; i++) {
            values.Add(ShapeValue(raw[i]));
        }

        values.Add(ShapeEdgeValue(raw[raw.Count - 1]));
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

    private static double ShapeValue(double x) => (Math.Atan(x * 0.3) * 0.4) / HalfPi;

    private static double ShapeEdgeValue(double x) => (Math.Atan(x * 0.15) * 0.4) / HalfPi;

    private static double SampleWrappedCubic(IReadOnlyList<double> values, double t) {
        var count = values.Count;
        var x = Fract(t) * count;
        var i = (int)Math.Floor(x);
        var f = x - i;

        var p0 = values[Mod(i - 1, count)];
        var p1 = values[Mod(i, count)];
        var p2 = values[Mod(i + 1, count)];
        var p3 = values[Mod(i + 2, count)];

        var f2 = f * f;
        var f3 = f2 * f;

        var value = p1
                    + (0.5 * f * (p2 - p0))
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

    private static Rgb LerpRgb(Rgb a, Rgb b, double t) {
        return new Rgb(Lerp(a.R, b.R, t), Lerp(a.G, b.G, t), Lerp(a.B, b.B, t));
    }

    private static Rgb MixRgb(Rgb a, Rgb b, double t) => LerpRgb(a, b, t);

    private static Rgb AlphaComposite(Rgb dst, Rgb src, double alpha) {
        return new Rgb(
            Lerp(dst.R, src.R, alpha),
            Lerp(dst.G, src.G, alpha),
            Lerp(dst.B, src.B, alpha));
    }

    private static double SmoothStep(double edge0, double edge1, double x) {
        var t = Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    private static double Clamp(double value, double min, double max) {
        return Math.Max(min, Math.Min(max, value));
    }

    private static double Fract(double value) => value - Math.Floor(value);

    private static int Mod(int value, int size) => ((value % size) + size) % size;

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private readonly record struct GradientStopModel(double T, double R, double G, double B);
    private readonly record struct Rgb(double R, double G, double B);
}
