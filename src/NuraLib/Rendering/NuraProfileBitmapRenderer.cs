using NuraLib.Devices;

namespace NuraLib.Rendering;

public sealed record class NuraProfileBitmap(int Width, int Height, byte[] Pixels) {
    /// <summary>
    /// Gets whether <see cref="Pixels"/> are premultiplied by their alpha channel.
    /// </summary>
    public bool IsPremultiplied { get; init; }

    public int Stride => Width * 4;
}

/// <summary>
/// CPU implementation of the Android static profile visualisation renderer.
/// </summary>
public static class NuraProfileBitmapRenderer {
    private const double TwoPi = Math.PI * 2.0;
    private const double EdgeSoftness = 0.005;
    private static readonly NuraProfileReferenceRgb NeutralCircleColour = new(0.31, 0.38, 0.47);

    public static NuraProfileBitmap Render(
        NuraProfileVisualisationData profile,
        double personalisationProgress,
        int size,
        bool useTransparency = false
    ) => Render(profile, profile, 1.0, personalisationProgress, size, useTransparency);

    public static NuraProfileBitmap Render(
        NuraProfileVisualisationData targetProfile,
        NuraProfileVisualisationData fromProfile,
        double profileBlendProgress,
        double personalisationProgress,
        int size,
        bool useTransparency = false
    ) {
        if (size <= 0) {
            throw new ArgumentOutOfRangeException(nameof(size), "Render size must be greater than zero.");
        }

        var frame = NuraProfileReferenceFrameFactory.Create(
            targetProfile,
            fromProfile,
            profileBlendProgress,
            personalisationProgress);
        var pixels = new byte[checked(size * size * 4)];

        for (var y = 0; y < size; y++) {
            // Android flips glReadPixels before constructing the returned Bitmap.
            var py = 1.0 - (((y + 0.5) / size) * 2.0);

            for (var x = 0; x < size; x++) {
                var px = (((x + 0.5) / size) * 2.0) - 1.0;
                var radius = Math.Sqrt((px * px) + (py * py)) * 2.0;
                var angle = NuraProfileReferenceMath.Fract(Math.Atan2(px, py) / TwoPi);
                var signature = frame.SampleSignature(angle);
                var colourSlide = frame.GetColourSlide(px, py, radius);
                var colourValue = new NuraProfileReferenceRgb(1.0, 1.0, 1.0);
                var blendMix = 1.0 - (py + 0.7);
                var opacity = 0.0;

                for (var contour = 0; contour < NuraProfileReferenceFrame.ContourCount; contour++) {
                    var edge = frame.GetContourRadius(contour, signature);
                    var solid = SmoothStep(EdgeSoftness, 0.0, radius - edge);
                    var alpha = frame.GetContourOpacity(contour);
                    var layerColour = frame.GetContourColour(contour);
                    var coverage = Math.Clamp(alpha * solid, 0.0, 1.0);

                    var alphaColour = NuraProfileReferenceMath.Lerp(colourValue, layerColour, alpha * solid);
                    var multipliedColour = NuraProfileReferenceMath.Lerp(colourValue, colourValue * layerColour, alpha * solid);
                    colourValue = NuraProfileReferenceMath.Lerp(alphaColour, multipliedColour, blendMix);
                    opacity = coverage + (opacity * (1.0 - coverage));
                }

                colourValue = NuraProfileReferenceMath.HueRotate(colourValue, -colourSlide);
                var neutralCoverage = SmoothStep(EdgeSoftness, 0.0, radius - NuraProfileReferenceFrame.BaseRadius);
                colourValue = NuraProfileReferenceMath.Lerp(NeutralCircleColour, colourValue, frame.Personalisation);
                opacity = NuraProfileReferenceMath.Lerp(neutralCoverage, opacity, frame.Personalisation);
                var pixelIndex = ((y * size) + x) * 4;
                var alphaValue = useTransparency ? opacity : 1.0;

                // The native static renderer composites over white. The optional transparent
                // output retains that profile colour but stores it as premultiplied BGRA.
                pixels[pixelIndex] = ToByte(Math.Clamp(colourValue.B, 0.0, 1.0) * alphaValue);
                pixels[pixelIndex + 1] = ToByte(Math.Clamp(colourValue.G, 0.0, 1.0) * alphaValue);
                pixels[pixelIndex + 2] = ToByte(Math.Clamp(colourValue.R, 0.0, 1.0) * alphaValue);
                pixels[pixelIndex + 3] = ToByte(alphaValue);
            }
        }

        return new NuraProfileBitmap(size, size, pixels) { IsPremultiplied = true };
    }

    /// <summary>
    /// Renders the static profile image
    /// </summary>
    public static NuraProfileBitmap Render(
        NuraProfileVisualisationData profile,
        int size,
        bool useTransparency = false
    ) => Render(profile, profile, 1.0, 1.0, size, useTransparency);

    private static double SmoothStep(double edge0, double edge1, double value) {
        var amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0, 1.0);
        return amount * amount * (3.0 - (2.0 * amount));
    }

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0.0, 1.0) * byte.MaxValue);
}
