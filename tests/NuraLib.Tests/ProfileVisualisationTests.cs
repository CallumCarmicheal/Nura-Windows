using NuraLib.Devices;
using NuraLib.Protocol;
using NuraLib.Rendering;
using NuraLib.Utilities;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal sealed class ProfileVisualisationTests {
    public void RunAll() {
        DecodeVisualisationData_ParsesCapturedPayload();
        ReferenceCurve_UsesNativeEdgeShapingWithoutNormalisation();
        ReferenceCurve_UsesNativeWrappedPhase();
        ReferenceCurve_CalculatesNativeSignatureOffset();
        ReferenceFrame_UsesNativeContourRadiiAndModeProgress();
        ReferenceFrame_BlendsProfilesBeforeTexturePacking();
        ReferenceBitmap_UsesOpaqueTopOriginOutput();
        ReferenceBitmap_OptInTransparency_UsesPremultipliedPixels();
        ReferenceBitmap_MatchesAndroidFixture_WhenAvailable();
    }

    private static void DecodeVisualisationData_ParsesCapturedPayload() {
        // Captured from the official Android app after decrypting GetVisualisationData.
        var payload = HexEncoding.Parse(
            "013cf4ea58c034c2dcbef275ff408db575c0b07e50c141a85fc1259351c15cf10fc12e42d3c16474d9c0e42fe6c03195d2bf9e9126c0c295a93f7d636dc0651636c158f16fc1266a0ac10c69b7c154623ac1c1fc1dc183e32ac1281bccc0d6fca3c0904105");
        var visualisation = NuraResponseParsers.DecodeVisualisationData(payload);

        AssertTrue(visualisation is not null, "Expected captured visualisation payload to decode.");
        AssertTrue(visualisation!.Valid, "Expected captured visualisation payload to be valid.");
        AssertNear(0.029896900057792664, visualisation.Colour, 0.000000001, "Unexpected colour scalar.");
        AssertNear(-2.8243932723999023, visualisation.LeftData[0], 0.000000001, "Unexpected first left value.");
        AssertNear(-14.27852725982666, visualisation.LeftData[8], 0.000000001, "Unexpected ninth left value.");
        AssertNear(-6.080769062042236, visualisation.RightData[0], 0.000000001, "Unexpected first right value.");
        AssertNear(-4.507936954498291, visualisation.RightData[11], 0.000000001, "Unexpected final right value.");
    }

    private static void ReferenceCurve_UsesNativeEdgeShapingWithoutNormalisation() {
        var raw = new[] { -6.0, -3.0, 0.0, 3.0, 6.0, 9.0, 12.0, 15.0, 18.0, 21.0, 24.0, 27.0 };
        var profile = new NuraProfileVisualisationData {
            Valid = true,
            LeftData = raw,
            RightData = raw
        };

        var values = NuraProfileReferenceCurve.CreateCombinedValues(profile);
        var expectedFirst = (Math.Atan(((-6.0 - 3.0 + 0.0) / 3.0) * 0.15) * 0.4) / (Math.PI / 2.0);
        var expectedInterior = (Math.Atan(3.0 * 0.3) * 0.4) / (Math.PI / 2.0);

        AssertEqual(10, values.Length, "Native curve should reduce twelve values to ten.");
        AssertNear(expectedFirst, values[0], 0.000000001, "Unexpected edge shaping.");
        AssertNear(expectedInterior, values[1], 0.000000001, "Unexpected interior shaping.");
        AssertTrue(values.Max(Math.Abs) < 0.4, "Reference curve must not normalise its peak to one.");
    }

    private static void ReferenceCurve_UsesNativeWrappedPhase() {
        var values = new[] { -0.3, -0.1, 0.2, 0.4 };

        AssertNear(0.4, NuraProfileReferenceCurve.SampleWrappedCubic(values, 0.0), 0.000000001, "Native curve should begin at the preceding value.");
        AssertNear(-0.3, NuraProfileReferenceCurve.SampleWrappedCubic(values, 1.0), 0.000000001, "Native curve should advance to the first value after one segment.");
    }

    private static void ReferenceCurve_CalculatesNativeSignatureOffset() {
        var offset = NuraProfileReferenceCurve.CalculateSignatureOffset(new[] { -0.3, -0.1, 0.2, 0.4 });
        AssertNear(-0.05, offset, 0.000000001, "Unexpected native signature offset.");
    }

    private static void ReferenceFrame_UsesNativeContourRadiiAndModeProgress() {
        var profile = CreateSyntheticProfile();
        var enabled = NuraProfileReferenceFrameFactory.Create(profile, profile, 1.0, 1.0);
        var disabled = NuraProfileReferenceFrameFactory.Create(profile, profile, 1.0, 0.0);
        var signature = enabled.SampleSignature(0.0);

        AssertNear(NuraProfileReferenceFrame.BaseRadius, enabled.GetContourRadius(0, signature), 0.000000001, "First contour must retain the native base radius.");
        AssertNear(
            NuraProfileReferenceFrame.BaseRadius + (signature * (5.0 / 6.0) * 2.0),
            enabled.GetContourRadius(5, signature),
            0.000000001,
            "Final contour must use the native contour factor.");
        AssertNear(NuraProfileReferenceFrame.BaseRadius, disabled.GetContourRadius(5, signature), 0.000000001, "Neutral mode must not distort profile radii.");
        AssertTrue(disabled.GetContourOpacity(0) < enabled.GetContourOpacity(0), "Neutral mode must use the native reduced opacity.");
    }

    private static void ReferenceFrame_BlendsProfilesBeforeTexturePacking() {
        var from = CreateSyntheticProfile();
        var sourceTarget = CreateSyntheticProfile();
        var target = sourceTarget with {
            LeftData = sourceTarget.LeftData.Select(value => value + 8.0).ToArray(),
            RightData = sourceTarget.RightData.Select(value => value - 5.0).ToArray(),
            Colour = 0.80
        };

        var start = NuraProfileReferenceFrameFactory.Create(target, from, 0.0, 1.0);
        var midpoint = NuraProfileReferenceFrameFactory.Create(target, from, 0.5, 1.0);
        var end = NuraProfileReferenceFrameFactory.Create(target, from, 1.0, 1.0);

        var startSignature = start.SampleSignature(0.375);
        var midpointSignature = midpoint.SampleSignature(0.375);
        var endSignature = end.SampleSignature(0.375);

        AssertTrue(Math.Abs(startSignature - endSignature) > 0.0001, "Distinct profiles must produce distinct native signatures.");
        AssertTrue(
            midpointSignature >= Math.Min(startSignature, endSignature) - 0.01 &&
            midpointSignature <= Math.Max(startSignature, endSignature) + 0.01,
            "Profile blending must occur before texture packing and remain between endpoint signatures.");
    }

    private static void ReferenceBitmap_UsesOpaqueTopOriginOutput() {
        var profile = CreateSyntheticProfile();
        var bitmap = NuraProfileBitmapRenderer.Render(profile, 64);

        AssertTrue(bitmap.Pixels.Where((_, index) => (index & 3) == 3).All(alpha => alpha == byte.MaxValue), "Native bitmap output should be opaque.");

        var top = ReadPixel(bitmap, 32, 13);
        var bottom = ReadPixel(bitmap, 32, 50);
        AssertTrue(top != bottom, "Top-origin renderer should preserve the native vertical orientation.");
    }

    private static void ReferenceBitmap_OptInTransparency_UsesPremultipliedPixels() {
        var profile = CreateSyntheticProfile();
        var bitmap = NuraProfileBitmapRenderer.Render(profile, 64, useTransparency: true);

        AssertTrue(bitmap.IsPremultiplied, "Transparent renderer output must be premultiplied BGRA.");
        AssertTrue(bitmap.Pixels.Where((_, index) => (index & 3) == 3).Any(alpha => alpha < byte.MaxValue), "Transparent output should contain non-opaque pixels.");

        for (var index = 0; index < bitmap.Pixels.Length; index += 4) {
            var alpha = bitmap.Pixels[index + 3];
            AssertTrue(bitmap.Pixels[index] <= alpha, "Premultiplied blue must not exceed alpha.");
            AssertTrue(bitmap.Pixels[index + 1] <= alpha, "Premultiplied green must not exceed alpha.");
            AssertTrue(bitmap.Pixels[index + 2] <= alpha, "Premultiplied red must not exceed alpha.");
        }
    }

    private static void ReferenceBitmap_MatchesAndroidFixture_WhenAvailable() {
        var fixturePath = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            "NuraLib.Tests",
            "Fixtures",
            "visualisation",
            "nura-reference-profile-256.png");

        if (!File.Exists(fixturePath)) {
            Console.WriteLine("Skipped Android visualisation golden test: fixture is not present.");
            return;
        }

        var expected = LoadBgraPixels(fixturePath, 256);
        var actual = NuraProfileBitmapRenderer.Render(CreateSyntheticProfile(), 256).Pixels;
        long totalError = 0;
        var largeErrorPixels = 0;

        for (var index = 0; index < actual.Length; index += 4) {
            var maxError = 0;
            for (var channel = 0; channel < 4; channel++) {
                var error = Math.Abs(actual[index + channel] - expected[index + channel]);
                totalError += error;
                maxError = Math.Max(maxError, error);
            }

            if (maxError > 8) {
                largeErrorPixels++;
            }
        }

        var meanChannelError = totalError / (double)actual.Length;
        var largeErrorRatio = largeErrorPixels / (256.0 * 256.0);

        AssertTrue(meanChannelError <= 1.0, $"Android fixture mean channel error was {meanChannelError:F3}.");
        AssertTrue(largeErrorRatio <= 0.005, $"Android fixture large-error ratio was {largeErrorRatio:P3}.");
    }

    private static NuraProfileVisualisationData CreateSyntheticProfile() => new() {
        Valid = true,
        Colour = 0.35,
        LeftData = [-8.0, -4.0, 0.0, 5.0, 10.0, 2.0, -6.0, -12.0, -4.0, 3.0, 8.0, 4.0],
        RightData = [-6.0, -2.0, 2.0, 7.0, 8.0, 0.0, -8.0, -10.0, -2.0, 5.0, 6.0, 2.0]
    };

    private static byte[] LoadBgraPixels(string path, int expectedSize) {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0.0);

        AssertEqual(expectedSize, source.PixelWidth, "Unexpected Android fixture width.");
        AssertEqual(expectedSize, source.PixelHeight, "Unexpected Android fixture height.");

        var pixels = new byte[expectedSize * expectedSize * 4];
        source.CopyPixels(pixels, expectedSize * 4, 0);
        return pixels;
    }

    private static (byte Blue, byte Green, byte Red, byte Alpha) ReadPixel(NuraProfileBitmap bitmap, int x, int y) {
        var offset = (y * bitmap.Stride) + (x * 4);
        return (bitmap.Pixels[offset], bitmap.Pixels[offset + 1], bitmap.Pixels[offset + 2], bitmap.Pixels[offset + 3]);
    }

    private static void AssertEqual<T>(T expected, T actual, string message) where T : IEquatable<T> {
        if (!expected.Equals(actual)) {
            throw new InvalidOperationException($"{message} Expected {expected}; got {actual}.");
        }
    }

    private static void AssertNear(double expected, double actual, double tolerance, string message) {
        if (Math.Abs(expected - actual) > tolerance) {
            throw new InvalidOperationException($"{message} Expected {expected}; got {actual}.");
        }
    }

    private static void AssertTrue(bool value, string message) {
        if (!value) {
            throw new InvalidOperationException(message);
        }
    }
}
