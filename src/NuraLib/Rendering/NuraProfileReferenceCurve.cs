using NuraLib.Devices;

namespace NuraLib.Rendering;

/// <summary>
/// Reproduces the profile-data path used by the Android native visualisation renderer.
/// </summary>
public static class NuraProfileReferenceCurve {
    public const int TextureSampleCount = 512;
    private const double PackingScale = 1.0039369473;

    internal static double[] CreateCombinedValues(NuraProfileVisualisationData profile) {
        ArgumentNullException.ThrowIfNull(profile);

        var left = MakeValues(profile.LeftData);
        var right = MakeValues(profile.RightData);
        var count = Math.Min(left.Length, right.Length);
        var values = new double[count];

        for (var index = 0; index < count; index++) {
            values[index] = (left[index] + right[index]) * 0.5;
        }

        return values;
    }

    internal static double[] MakeValues(IReadOnlyList<double> raw) {
        ArgumentNullException.ThrowIfNull(raw);

        if (raw.Count < 4) {
            throw new InvalidOperationException("Profile data must contain at least four values.");
        }

        var values = new double[raw.Count - 2];
        values[0] = ShapeEdge((raw[0] + raw[1] + raw[2]) / 3.0);

        for (var sourceIndex = 3; sourceIndex <= raw.Count - 2; sourceIndex++) {
            values[sourceIndex - 2] = ShapeInterior(raw[sourceIndex]);
        }

        values[^1] = ShapeEdge(raw[^1]);
        return values;
    }

    internal static NuraProfilePackedSample[] CreateTextureSamples(IReadOnlyList<double> values) {
        ArgumentNullException.ThrowIfNull(values);

        var samples = new NuraProfilePackedSample[TextureSampleCount];
        for (var index = 0; index < samples.Length; index++) {
            var coordinate = (index / (double)samples.Length) * values.Count;
            samples[index] = Pack(SampleWrappedCubic(values, coordinate));
        }

        return samples;
    }

    internal static double SampleTexture(IReadOnlyList<NuraProfilePackedSample> samples, double coordinate) {
        ArgumentNullException.ThrowIfNull(samples);

        var normalized = Fract(coordinate);
        var position = (normalized * samples.Count) - 0.5;
        var rawLeftIndex = (int)Math.Floor(position);
        var leftIndex = Math.Clamp(rawLeftIndex, 0, samples.Count - 1);
        var rightIndex = Math.Clamp(rawLeftIndex + 1, 0, samples.Count - 1);
        var fraction = position - Math.Floor(position);

        var packed = new NuraProfilePackedSample(
            Lerp(samples[leftIndex].Red, samples[rightIndex].Red, fraction),
            Lerp(samples[leftIndex].Green, samples[rightIndex].Green, fraction),
            Lerp(samples[leftIndex].Blue, samples[rightIndex].Blue, fraction));

        return Decode(packed);
    }

    internal static double CalculateSignatureOffset(IReadOnlyList<double> values, double progress = 1.0) {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0) {
            return 0.0;
        }

        var minimum = values.Min();
        var maximum = values.Max();
        return -0.5 * (minimum + maximum) * progress;
    }

    internal static double SampleWrappedCubic(IReadOnlyList<double> values, double coordinate) {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count < 2) {
            throw new InvalidOperationException("Profile curve data must contain at least two values.");
        }

        var wrappedCoordinate = Fract(coordinate / values.Count) * values.Count;
        var index = (int)wrappedCoordinate;
        var fraction = wrappedCoordinate - index;
        var count = values.Count;

        // This is the Android CurveInterpolator::processCubicWrapped ordering. It starts
        // from the preceding point, which deliberately shifts the top of the signature.
        var previousPrevious = values[index + (index < 2 ? count - 2 : -2)];
        var current = values[index];
        var previous = values[index - (index < 1 ? 1 - count : 1)];
        var next = values[index + ((count - 1 - index) >= 1 ? 1 : 1 - count)];

        var cubic = previous + ((next - current) - previousPrevious);
        var result = previous
            + (fraction * fraction * ((previousPrevious - previous) - cubic))
            + (fraction * fraction * fraction * cubic)
            + (fraction * (current - previousPrevious));

        return Math.Clamp(result, -1.0, 1.0);
    }

    private static double ShapeInterior(double value) => (Math.Atan(value * 0.3) * 0.4) / (Math.PI / 2.0);

    private static double ShapeEdge(double value) => (Math.Atan(value * 0.15) * 0.4) / (Math.PI / 2.0);

    private static NuraProfilePackedSample Pack(double value) {
        var packed = Math.Min(((value * 0.5) + 0.5) * PackingScale, 1.0);
        var red = QuantizeHalf(packed);

        packed -= red;
        var green = QuantizeHalf(Math.Min(packed * 255.0, 1.0));

        packed = Math.Min((packed - (green / 255.0)) * 65025.0, 1.0);
        var blue = QuantizeHalf(packed);

        return new NuraProfilePackedSample(red, green, blue);
    }

    private static double Decode(NuraProfilePackedSample packed) =>
        (packed.Red + (packed.Green / 255.0) + (packed.Blue / 65025.0)) / PackingScale;

    private static double QuantizeHalf(double value) => (float)(Half)(float)value;

    private static double Fract(double value) => value - Math.Floor(value);

    private static double Lerp(double from, double to, double amount) => from + ((to - from) * amount);
}

public readonly record struct NuraProfilePackedSample(double Red, double Green, double Blue);
