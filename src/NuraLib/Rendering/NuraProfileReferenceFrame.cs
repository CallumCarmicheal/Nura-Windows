using NuraLib.Devices;

namespace NuraLib.Rendering;

/// <summary>
/// Shared native visualisation state for bitmap and retained-shape renderers.
/// </summary>
public class NuraProfileReferenceFrame {
    public const int ContourCount = 6;
    public const double BaseRadius = 1.15;
    public const double ContourStep = 1.0 / ContourCount;
    public const double BaseAlpha = 0.4;
    public const double AlphaSink = 0.15;
    public const double HueSlideAmount = 0.07;

    public NuraProfileReferenceFrame(
        IReadOnlyList<NuraProfilePackedSample> textureSamples,
        double signatureOffset,
        double personalisation,
        double greyProgress,
        NuraProfileReferenceRgb firstColour,
        NuraProfileReferenceRgb secondColour,
        double totalCos,
        double totalSin
    ) {
        TextureSamples = textureSamples;
        SignatureOffset = signatureOffset;
        Personalisation = personalisation;
        GreyProgress = greyProgress;
        FirstColour = firstColour;
        SecondColour = secondColour;
        TotalCos = totalCos;
        TotalSin = totalSin;
    }

    public IReadOnlyList<NuraProfilePackedSample> TextureSamples { get; }
    public double SignatureOffset { get; }
    public double Personalisation { get; }
    public double GreyProgress { get; }
    public NuraProfileReferenceRgb FirstColour { get; }
    public NuraProfileReferenceRgb SecondColour { get; }
    public double TotalCos { get; }
    public double TotalSin { get; }

    public double SampleSignature(double angle) =>
        ((NuraProfileReferenceCurve.SampleTexture(TextureSamples, angle) * 2.0) - 1.0) + SignatureOffset;

    public double GetContourRadius(int contourIndex, double signature) {
        var contourValue = contourIndex * ContourStep;
        return BaseRadius + ((signature * contourValue * 2.0) * Personalisation);
    }

    public double GetContourOpacity(int contourIndex) =>
        (BaseAlpha - (AlphaSink * (contourIndex * ContourStep))) * GreyProgress;

    public NuraProfileReferenceRgb GetContourColour(int contourIndex) {
        var contourValue = contourIndex * ContourStep;
        return contourIndex == 0
            ? FirstColour * Personalisation
            : NuraProfileReferenceMath.Lerp(FirstColour, SecondColour, contourValue * 1.3) * Personalisation;
    }

    public double GetColourSlide(double px, double py, double radius) {
        var rotatedX = (TotalCos * px) - (TotalSin * py);
        var rotatedY = (TotalSin * px) + (TotalCos * py);
        return (rotatedX + rotatedY + (radius * 0.2)) * HueSlideAmount;
    }
}

public static class NuraProfileReferenceFrameFactory {
    public static NuraProfileReferenceFrame Create(
        NuraProfileVisualisationData targetProfile,
        NuraProfileVisualisationData fromProfile,
        double profileBlendProgress,
        double personalisationProgress
    ) {
        ArgumentNullException.ThrowIfNull(targetProfile);
        ArgumentNullException.ThrowIfNull(fromProfile);

        var blend = Math.Clamp(profileBlendProgress, 0.0, 1.0);
        var personalisation = Math.Clamp(personalisationProgress, 0.0, 1.0);
        var sourceValues = NuraProfileReferenceCurve.CreateCombinedValues(fromProfile);
        var targetValues = NuraProfileReferenceCurve.CreateCombinedValues(targetProfile);
        var values = BlendValues(sourceValues, targetValues, blend);
        var colour = NuraProfileReferenceMath.Lerp(fromProfile.Colour, targetProfile.Colour, blend);
        var (firstColour, secondColour) = GetProfileColours(colour);
        var total = values.Sum();

        return new NuraProfileReferenceFrame(
            NuraProfileReferenceCurve.CreateTextureSamples(values),
            NuraProfileReferenceCurve.CalculateSignatureOffset(values),
            personalisation,
            (personalisation * 1.0) + ((1.0 - personalisation) * 0.03),
            firstColour,
            secondColour,
            Math.Cos(total),
            Math.Sin(total));
    }

    private static double[] BlendValues(IReadOnlyList<double> source, IReadOnlyList<double> target, double blend) {
        var count = Math.Min(source.Count, target.Count);
        var values = new double[count];

        for (var index = 0; index < count; index++) {
            values[index] = NuraProfileReferenceMath.Lerp(source[index], target[index], blend);
        }

        return values;
    }

    private static (NuraProfileReferenceRgb First, NuraProfileReferenceRgb Second) GetProfileColours(double colour) {
        var hue = NuraProfileReferenceMath.Fract(0.35 + Math.Clamp(colour, 0.0, 1.0));
        var first = SampleGradient(Math.Abs(1.0 - (hue * 2.0)));
        var secondHue = NuraProfileReferenceMath.Fract(hue + 0.65);
        var second = SampleGradient(Math.Abs(1.0 - (secondHue * 2.0))) * 1.3;
        return (first, second);
    }

    private static NuraProfileReferenceRgb SampleGradient(double position) {
        var clamped = Math.Clamp(position, 0.0, 1.0);

        for (var index = 0; index < GradientStops.Length - 1; index++) {
            var start = GradientStops[index];
            var end = GradientStops[index + 1];
            if (clamped < start.Position || clamped > end.Position) {
                continue;
            }

            var amount = (clamped - start.Position) / (end.Position - start.Position);
            return NuraProfileReferenceMath.Lerp(start.Colour, end.Colour, amount);
        }

        return GradientStops[^1].Colour;
    }

    private static readonly NuraProfileGradientStop[] GradientStops = [
        new(0.00, 240, 127, 87),
        new(0.12, 236, 28, 36),
        new(0.28, 238, 88, 149),
        new(0.41, 160, 76, 156),
        new(0.60, 61, 83, 163),
        new(0.80, 8, 152, 205),
        new(1.00, 141, 209, 199)
    ];
}

public static class NuraProfileReferenceMath {
    public static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);

    public static NuraProfileReferenceRgb Lerp(NuraProfileReferenceRgb from, NuraProfileReferenceRgb to, double amount) => new(
        Lerp(from.R, to.R, amount),
        Lerp(from.G, to.G, amount),
        Lerp(from.B, to.B, amount));

    public static double Fract(double value) => value - Math.Floor(value);

    public static NuraProfileReferenceRgb HueRotate(NuraProfileReferenceRgb colour, double amount) {
        var hsv = RgbToHsv(colour);
        return HsvToRgb(new NuraProfileReferenceRgb(Fract(hsv.R + amount), hsv.G, hsv.B));
    }

    private static NuraProfileReferenceRgb RgbToHsv(NuraProfileReferenceRgb colour) {
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

        return new NuraProfileReferenceRgb(hue, maximum <= 0.0000000001 ? 0.0 : delta / maximum, maximum);
    }

    private static NuraProfileReferenceRgb HsvToRgb(NuraProfileReferenceRgb hsv) {
        var h = Fract(hsv.R) * 6.0;
        var c = hsv.B * hsv.G;
        var x = c * (1.0 - Math.Abs((h % 2.0) - 1.0));
        var m = hsv.B - c;
        var sector = (int)Math.Floor(h);

        var rgb = sector switch {
            0 => new NuraProfileReferenceRgb(c, x, 0.0),
            1 => new NuraProfileReferenceRgb(x, c, 0.0),
            2 => new NuraProfileReferenceRgb(0.0, c, x),
            3 => new NuraProfileReferenceRgb(0.0, x, c),
            4 => new NuraProfileReferenceRgb(x, 0.0, c),
            _ => new NuraProfileReferenceRgb(c, 0.0, x)
        };

        return new NuraProfileReferenceRgb(rgb.R + m, rgb.G + m, rgb.B + m);
    }
}

public readonly record struct NuraProfileReferenceRgb(double R, double G, double B) {
    public static NuraProfileReferenceRgb operator *(NuraProfileReferenceRgb colour, double multiplier) => new(
        colour.R * multiplier,
        colour.G * multiplier,
        colour.B * multiplier);

    public static NuraProfileReferenceRgb operator *(NuraProfileReferenceRgb left, NuraProfileReferenceRgb right) => new(
        left.R * right.R,
        left.G * right.G,
        left.B * right.B);
}

public readonly record struct NuraProfileGradientStop(double Position, double Red, double Green, double Blue) {
    public NuraProfileReferenceRgb Colour => new(Red / byte.MaxValue, Green / byte.MaxValue, Blue / byte.MaxValue);
}
