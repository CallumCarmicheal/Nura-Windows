using NuraLib.Devices;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace NuraLib.Rendering;

public sealed record class NuraProfileBitmap {
    public NuraProfileBitmap(
        int width,
        int height,
        byte[] pixels
    ) {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    /// <summary>
    /// Gets BGRA32 pixel data.
    /// </summary>
    public byte[] Pixels { get; }
}

public class NuraProfileBitmapRenderer {
    private const double HalfPi = Math.PI / 2.0;

    private static readonly GradientStopModel[] GradientStops = {
        new(0.00, 240, 127, 87),
        new(0.12, 236, 28, 36),
        new(0.28, 238, 88, 149),
        new(0.41, 160, 76, 156),
        new(0.60, 61, 83, 163),
        new(0.80, 8, 152, 205),
        new(1.00, 141, 209, 199),
    };

    public NuraProfileBitmap Render(
        NuraProfileVisualisationData profile,
        double personalisationProgress,
        int size,
        double immersionValue
    ) => Render(profile, profile, 1, personalisationProgress, size, immersionValue);

    public NuraProfileBitmap Render(
        NuraProfileVisualisationData targetProfile,
        NuraProfileVisualisationData fromProfile,
        double profileBlendProgress,
        double personalisationProgress,
        int size,
        double immersionValue
    ) {
        int width = size;
        int height = size;
        int stride = width * 4;

        byte[] pixels = new byte[stride * height];

        RenderVisualisation(
            targetProfile,
            fromProfile,
            profileBlendProgress,
            personalisationProgress,
            size,
            immersionValue,
            pixels
        );

        return new NuraProfileBitmap(size, size, pixels);
    }

    /// <summary>
    /// Render the nura profile into a bitmap object, use the extensions under NuraLib.Rendering.Drawing for common conversions
    /// </summary>
    /// <param name="profile"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public NuraProfileBitmap RenderThumbnail(NuraProfileVisualisationData profile, int size) {
        return Render(
            profile,
            profile,
            profileBlendProgress: 1.0,
            personalisationProgress: 1.0,
            size,
            immersionValue: 1.0
        );
    }

    private void RenderVisualisation(
        NuraProfileVisualisationData targetProfile,
        NuraProfileVisualisationData fromProfile,
        double profileBlendProgress,
        double personalisationProgress,
        int size,
        double immersionValue,
        byte[] pixels
    ) {
        int width = size;
        int height = size;
        int stride = width * 4;

        if (pixels.Length < stride * height) {
            throw new ArgumentException(
                "Pixel buffer is smaller than the requested render size.",
                nameof(pixels)
            );
        }

        Array.Clear(pixels, 0, stride * height);

        double[] targetCurve = BuildProfileCurve(targetProfile.LeftData, targetProfile.RightData);
        double[] sourceCurve = BuildProfileCurve(fromProfile.LeftData, fromProfile.RightData);
        double[] blendedCurve = new double[targetCurve.Length];

        for (int i = 0; i < targetCurve.Length; i++) {
            blendedCurve[i] = Lerp(sourceCurve[i], targetCurve[i], profileBlendProgress) * personalisationProgress;
        }

        double fromColour = Lerp(0.7, fromProfile.Colour, personalisationProgress);
        double toColour = Lerp(0.7, targetProfile.Colour, personalisationProgress);
        double transitionColour = Lerp(fromColour, toColour, profileBlendProgress);

        var colours = GetProfileColours(transitionColour);

        double baseRadius = 1.15;
        double profileScale = Lerp(
            0.0,
            Lerp(0.32, 0.58, (immersionValue + 2.0) / 6.0),
            personalisationProgress
        );

        double edgeSoftness = 0.028;
        int bandCount = 6;
        double baseAlpha = Lerp(0.34, 0.48, personalisationProgress);
        double alphaSink = 0.13;

        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                double uvX = (x + 0.5) / width;
                double uvY = (y + 0.5) / height;

                double px = (uvX - 0.5) * 2.0;
                double py = (uvY - 0.5) * 2.0;

                double radius = Math.Sqrt((px * px) + (py * py)) * 2.0;
                double angle = Fract(Math.Atan2(px, py) / (Math.PI * 2.0));
                double profileValue = SampleWrappedCubic(blendedCurve, angle);

                var color = new Rgb(0, 0, 0);

                for (int band = 0; band < bandCount; band++) {
                    double fi = band / (double)bandCount;
                    double bandOffset = profileValue * fi * profileScale;
                    double edge = baseRadius + bandOffset;
                    double mask = SmoothStep(edge + edgeSoftness, edge - edgeSoftness, radius);
                    double alpha = Math.Max(0.0, baseAlpha - (alphaSink * fi)) * mask;

                    Rgb bandColor = MixRgb(colours.Col1, colours.Col2, fi);
                    color = AlphaComposite(color, bandColor, alpha);
                }

                double glow = Clamp(1.0 - (Math.Abs(radius - baseRadius) / 0.23), 0.0, 1.0) * 0.06;
                color = AlphaComposite(color, MixRgb(colours.Col1, colours.Col2, 0.5), glow);

                double intensity = Math.Max(color.R, Math.Max(color.G, color.B)) / 255.0;
                double softField = Clamp(1.0 - (Math.Abs(radius - baseRadius) / 0.42), 0.0, 1.0) * 0.18;
                double alphaChannel = Clamp((intensity * 1.2) + softField, 0.0, 1.0);

                int index = (y * stride) + (x * 4);

                // System.Drawing 32bppArgb memory layout is BGRA.
                pixels[index + 0] = (byte)Math.Round(color.B);
                pixels[index + 1] = (byte)Math.Round(color.G);
                pixels[index + 2] = (byte)Math.Round(color.R);
                pixels[index + 3] = (byte)Math.Round(alphaChannel * 255.0);
            }
        }
    }

    private static double[] BuildProfileCurve(IReadOnlyList<double> leftData, IReadOnlyList<double> rightData) {
        double[] left = MakeValues(leftData);
        double[] right = MakeValues(rightData);

        int count = Math.Min(left.Length, right.Length);
        double[] values = new double[count];

        for (int i = 0; i < count; i++) {
            values[i] = (left[i] + right[i]) * 0.5;
        }

        return NormalizeCurve(values);
    }

    private static double[] MakeValues(IReadOnlyList<double> raw) {
        if (raw.Count < 4) {
            throw new InvalidOperationException("Profile data must contain at least 4 values.");
        }

        var values = new List<double>();

        double first = (raw[0] + raw[1] + raw[2]) / 3.0;
        values.Add(ShapeEdgeValue(first));

        for (int i = 3; i <= raw.Count - 2; i++) {
            values.Add(ShapeValue(raw[i]));
        }

        values.Add(ShapeEdgeValue(raw[^1]));

        return values.ToArray();
    }

    private static double[] NormalizeCurve(IReadOnlyList<double> values) {
        double maxAbs = 0.0;

        foreach (double value in values) {
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
        int count = values.Count;

        double x = Fract(t) * count;
        int i = (int)Math.Floor(x);
        double f = x - i;

        double p0 = values[Mod(i - 1, count)];
        double p1 = values[Mod(i, count)];
        double p2 = values[Mod(i + 1, count)];
        double p3 = values[Mod(i + 2, count)];

        double f2 = f * f;
        double f3 = f2 * f;

        double value =
            p1
            + (0.5 * f * (p2 - p0))
            + (0.5 * f2 * ((2.0 * p0) - (5.0 * p1) + (4.0 * p2) - p3))
            + (0.5 * f3 * (-p0 + (3.0 * p1) - (3.0 * p2) + p3));

        return Clamp(value, -1.0, 1.0);
    }

    private static (Rgb Col1, Rgb Col2) GetProfileColours(double colour) {
        double baseHue = 0.35;
        double gradientShift = 0.65;
        double c = Clamp(colour, 0.0, 1.0);

        double hue1 = Fract(baseHue + c);
        double hue2 = Fract(hue1 + gradientShift);

        double t1 = Math.Abs(1.0 - (hue1 * 2.0));
        double t2 = Math.Abs(1.0 - (hue2 * 2.0));

        return (SampleGradient(t1), SampleGradient(t2));
    }

    private static Rgb SampleGradient(double t) {
        double x = Clamp(t, 0.0, 1.0);

        for (int i = 0; i < GradientStops.Length - 1; i++) {
            GradientStopModel a = GradientStops[i];
            GradientStopModel b = GradientStops[i + 1];

            if (x >= a.T && x <= b.T) {
                double localT = (x - a.T) / (b.T - a.T);

                return LerpRgb(
                    new Rgb(a.R, a.G, a.B),
                    new Rgb(b.R, b.G, b.B),
                    localT
                );
            }
        }

        GradientStopModel last = GradientStops[^1];

        return new Rgb(last.R, last.G, last.B);
    }

    private static Rgb LerpRgb(Rgb a, Rgb b, double t) {
        return new Rgb(
            Lerp(a.R, b.R, t),
            Lerp(a.G, b.G, t),
            Lerp(a.B, b.B, t)
        );
    }

    private static Rgb MixRgb(Rgb a, Rgb b, double t) => LerpRgb(a, b, t);

    private static Rgb AlphaComposite(Rgb dst, Rgb src, double alpha)
        => new Rgb(Lerp(dst.R, src.R, alpha), Lerp(dst.G, src.G, alpha), Lerp(dst.B, src.B, alpha));

    private static double SmoothStep(double edge0, double edge1, double x) {
        double t = Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);

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
