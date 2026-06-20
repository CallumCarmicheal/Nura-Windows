using NuraLib.Devices;

namespace NuraLib.Rendering;

public sealed record class NuraProfileBitmap(int Width, int Height, byte[] Pixels) {
    public int Stride => Width * 4;
}

/// <summary>
/// CPU implementation of the Android static profile visualisation renderer.
/// </summary>
public class NuraProfileBitmapRenderer {
    private const double TwoPi = Math.PI * 2.0;
    private const double BaseRadius = 1.15;
    private const double EdgeSoftness = 0.005;
    private const double ContourStep = 1.0 / 6.0;
    private const double HueSlideAmount = 0.07;
    private const double BaseAlpha = 0.4;
    private const double AlphaSink = 0.15;
    private const double HueOffset = 0.35;
    private const double GradientShift = 0.65;

    private static readonly GradientStop[] GradientStops = [
        new(0.00, 240, 127, 87),
        new(0.12, 236, 28, 36),
        new(0.28, 238, 88, 149),
        new(0.41, 160, 76, 156),
        new(0.60, 61, 83, 163),
        new(0.80, 8, 152, 205),
        new(1.00, 141, 209, 199)
    ];

    public NuraProfileBitmap Render(
        NuraProfileVisualisationData profile,
        double personalisationProgress,
        int size,
        double immersionValue
    ) => Render(profile, profile, 1.0, personalisationProgress, size, immersionValue);

    public NuraProfileBitmap Render(
        NuraProfileVisualisationData targetProfile,
        NuraProfileVisualisationData fromProfile,
        double profileBlendProgress,
        double personalisationProgress,
        int size,
        double immersionValue
    ) {
        ArgumentNullException.ThrowIfNull(targetProfile);
        ArgumentNullException.ThrowIfNull(fromProfile);

        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "Render size must be greater than zero.");
        }

        // The official static Android renderer has no immersion input. Keep this parameter
        // for source compatibility with existing callers but intentionally do not use it.
        _ = immersionValue;

        var blend = Math.Clamp(profileBlendProgress, 0.0, 1.0);
        var personalisation = Math.Clamp(personalisationProgress, 0.0, 1.0);
        var sourceValues = NuraProfileReferenceCurve.CreateCombinedValues(fromProfile);
        var targetValues = NuraProfileReferenceCurve.CreateCombinedValues(targetProfile);
        var values = BlendValues(sourceValues, targetValues, blend);
        var textureSamples = NuraProfileReferenceCurve.CreateTextureSamples(values);
        var signatureOffset = NuraProfileReferenceCurve.CalculateSignatureOffset(values);
        var colour = Lerp(fromProfile.Colour, targetProfile.Colour, blend);
        var colours = GetProfileColours(colour);
        var total = values.Sum();
        var totalCos = Math.Cos(total);
        var totalSin = Math.Sin(total);
        var greyProgress = (personalisation * 1.0) + ((1.0 - personalisation) * 0.03);
        var pixels = new byte[checked(size * size * 4)];

        for (var y = 0; y < size; y++) {
            // Android flips glReadPixels before constructing the returned Bitmap.
            var py = 1.0 - (((y + 0.5) / size) * 2.0);

            for (var x = 0; x < size; x++) {
                var px = (((x + 0.5) / size) * 2.0) - 1.0;
                var radius = Math.Sqrt((px * px) + (py * py)) * 2.0;
                var rawAngle = Math.Atan2(px, py) / TwoPi;
                var angle = Fract(rawAngle);
                var sample = NuraProfileReferenceCurve.SampleTexture(textureSamples, angle);
                var signature = ((sample * 2.0) - 1.0) + signatureOffset;

                var rotatedX = (totalCos * px) - (totalSin * py);
                var rotatedY = (totalSin * px) + (totalCos * py);
                var colourSlide = (rotatedX + rotatedY + (radius * 0.2)) * HueSlideAmount;
                var colourValue = new Rgb(1.0, 1.0, 1.0);
                var blendMix = 1.0 - (py + 0.7);

                for (var contour = 0; contour < 6; contour++) {
                    var contourValue = contour * ContourStep;
                    var value = signature * contourValue;
                    var edge = BaseRadius + ((value * 2.0) * personalisation);
                    var solid = SmoothStep(EdgeSoftness, 0.0, radius - edge);
                    var alpha = (BaseAlpha - (AlphaSink * contourValue)) * greyProgress;
                    var layerColour = contour == 0
                        ? colours.First * personalisation
                        : Lerp(colours.First, colours.Second, contourValue * 1.3) * personalisation;

                    var alphaColour = Lerp(colourValue, layerColour, alpha * solid);
                    var multipliedColour = Lerp(colourValue, colourValue * layerColour, alpha * solid);
                    colourValue = Lerp(alphaColour, multipliedColour, blendMix);
                }

                colourValue = HueRotate(colourValue, -colourSlide);
                var pixelIndex = ((y * size) + x) * 4;
                pixels[pixelIndex] = ToByte(colourValue.B);
                pixels[pixelIndex + 1] = ToByte(colourValue.G);
                pixels[pixelIndex + 2] = ToByte(colourValue.R);
                pixels[pixelIndex + 3] = byte.MaxValue;
            }
        }

        return new NuraProfileBitmap(size, size, pixels);
    }

    /// <summary>
    /// Renders the static profile image used for profile previews and exports.
    /// </summary>
    public NuraProfileBitmap RenderThumbnail(NuraProfileVisualisationData profile, int size) =>
        Render(profile, profile, 1.0, 1.0, size, immersionValue: 0.0);

    private static double[] BlendValues(IReadOnlyList<double> source, IReadOnlyList<double> target, double blend) {
        var count = Math.Min(source.Count, target.Count);
        var values = new double[count];

        for (var index = 0; index < count; index++) {
            values[index] = Lerp(source[index], target[index], blend);
        }

        return values;
    }

    private static (Rgb First, Rgb Second) GetProfileColours(double colour) {
        var hue = Fract(HueOffset + Math.Clamp(colour, 0.0, 1.0));
        var first = SampleGradient(Math.Abs(1.0 - (hue * 2.0)));
        var secondHue = Fract(hue + GradientShift);
        var second = SampleGradient(Math.Abs(1.0 - (secondHue * 2.0))) * 1.3;
        return (first, second);
    }

    private static Rgb SampleGradient(double position) {
        var clamped = Math.Clamp(position, 0.0, 1.0);

        for (var index = 0; index < GradientStops.Length - 1; index++) {
            var start = GradientStops[index];
            var end = GradientStops[index + 1];
            if (clamped < start.Position || clamped > end.Position) {
                continue;
            }

            var amount = (clamped - start.Position) / (end.Position - start.Position);
            return Lerp(start.Colour, end.Colour, amount);
        }

        return GradientStops[^1].Colour;
    }

    private static Rgb HueRotate(Rgb colour, double amount) {
        var hsv = RgbToHsv(colour);
        return HsvToRgb(new Rgb(Fract(hsv.R + amount), hsv.G, hsv.B));
    }

    private static Rgb RgbToHsv(Rgb colour) {
        var maximum = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        var minimum = Math.Min(colour.R, Math.Min(colour.G, colour.B));
        var delta = maximum - minimum;

        double hue;
        if (delta < 0.0000000001) {
            hue = 0.0;
        } else if (maximum == colour.R) {
            hue = Fract(((colour.G - colour.B) / delta) / 6.0);
        } else if (maximum == colour.G) {
            hue = (((colour.B - colour.R) / delta) + 2.0) / 6.0;
        } else {
            hue = (((colour.R - colour.G) / delta) + 4.0) / 6.0;
        }

        return new Rgb(hue, maximum <= 0.0000000001 ? 0.0 : delta / maximum, maximum);
    }

    private static Rgb HsvToRgb(Rgb hsv) {
        var h = Fract(hsv.R) * 6.0;
        var c = hsv.B * hsv.G;
        var x = c * (1.0 - Math.Abs((h % 2.0) - 1.0));
        var m = hsv.B - c;
        var sector = (int)Math.Floor(h);

        var rgb = sector switch {
            0 => new Rgb(c, x, 0.0),
            1 => new Rgb(x, c, 0.0),
            2 => new Rgb(0.0, c, x),
            3 => new Rgb(0.0, x, c),
            4 => new Rgb(x, 0.0, c),
            _ => new Rgb(c, 0.0, x)
        };

        return new Rgb(rgb.R + m, rgb.G + m, rgb.B + m);
    }

    private static double SmoothStep(double edge0, double edge1, double value) {
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return amount * amount * (3.0 - (2.0 * amount));
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0.0, 1.0) * byte.MaxValue);

    private static double Fract(double value) => value - Math.Floor(value);

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    private static Rgb Lerp(Rgb from, Rgb to, double amount) => new(
        Lerp(from.R, to.R, amount),
        Lerp(from.G, to.G, amount),
        Lerp(from.B, to.B, amount));

    private readonly record struct GradientStop(double Position, double Red, double Green, double Blue) {
        public Rgb Colour => new(Red / byte.MaxValue, Green / byte.MaxValue, Blue / byte.MaxValue);
    }

    private readonly record struct Rgb(double R, double G, double B) {
        public static Rgb operator *(Rgb colour, double multiplier) => new(
            colour.R * multiplier,
            colour.G * multiplier,
            colour.B * multiplier);

        public static Rgb operator *(Rgb left, Rgb right) => new(
            left.R * right.R,
            left.G * right.G,
            left.B * right.B);
    }
}
