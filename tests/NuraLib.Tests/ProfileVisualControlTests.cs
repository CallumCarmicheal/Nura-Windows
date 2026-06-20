using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NuraLib.Devices;

using NuraPopupWpf.Controls;
using NuraPopupWpf.Models;

internal sealed class ProfileVisualControlTests {
    public void RunAll() {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                ShapeRenderer_RespectsTransparentAndWhiteBackgrounds();
            } catch (Exception exception) {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null) {
            throw new InvalidOperationException("WPF profile visual control test failed.", failure);
        }
    }

    private static void ShapeRenderer_RespectsTransparentAndWhiteBackgrounds() {
        var profile = new ProfileModel("Test", new NuraProfileVisualisationData {
            Valid = true,
            Colour = 0.35,
            LeftData = [-8.0, -4.0, 0.0, 5.0, 10.0, 2.0, -6.0, -12.0, -4.0, 3.0, 8.0, 4.0],
            RightData = [-6.0, -2.0, 2.0, 7.0, 8.0, 0.0, -8.0, -10.0, -2.0, 5.0, 6.0, 2.0]
        });

        var transparent = Render(profile, useBitmapRenderer: false, backgroundMode: ProfileVisualBackgroundMode.Transparent);
        var white = Render(profile, useBitmapRenderer: false, backgroundMode: ProfileVisualBackgroundMode.White);
        var bitmap = Render(profile, useBitmapRenderer: true, backgroundMode: ProfileVisualBackgroundMode.Transparent);
        var neutral = Render(profile, useBitmapRenderer: false, backgroundMode: ProfileVisualBackgroundMode.Transparent, modeProgress: 0.0);

        AssertTrue(
            transparent.Where((_, index) => (index & 3) == 3).Any(alpha => alpha < byte.MaxValue),
            "Transparent shape render must retain transparent pixels.");
        AssertTrue(
            white.Where((_, index) => (index & 3) == 3).All(alpha => alpha == byte.MaxValue),
            "White shape render must be fully opaque.");
        AssertTrue(
            Enumerable.Range(0, neutral.Length / 4).Any(index =>
                neutral[(index * 4) + 3] >= 8 &&
                neutral[(index * 4) + 0] > neutral[(index * 4) + 2] &&
                neutral[(index * 4) + 1] > neutral[(index * 4) + 2]),
            "Neutral shape render must retain a visible blue-grey circle on transparency.");

        var shapeBounds = FindVisibleBounds(transparent, 128, alphaCutoff: 8);
        var bitmapBounds = FindVisibleBounds(bitmap, 128, alphaCutoff: 8);
        AssertTrue(Math.Abs(shapeBounds.Left - bitmapBounds.Left) <= 2, "Shape and bitmap left contour bounds must align.");
        AssertTrue(Math.Abs(shapeBounds.Top - bitmapBounds.Top) <= 2, "Shape and bitmap top contour bounds must align.");
        AssertTrue(Math.Abs(shapeBounds.Right - bitmapBounds.Right) <= 2, "Shape and bitmap right contour bounds must align.");
        AssertTrue(Math.Abs(shapeBounds.Bottom - bitmapBounds.Bottom) <= 2, "Shape and bitmap bottom contour bounds must align.");
    }

    private static byte[] Render(
        ProfileModel profile,
        bool useBitmapRenderer,
        ProfileVisualBackgroundMode backgroundMode,
        double modeProgress = 1.0
    ) {
        const int size = 128;
        var control = new ProfileVisualControl {
            Width = size,
            Height = size,
            FromProfile = profile,
            ToProfile = profile,
            ProfileBlendProgress = 1.0,
            ModeProgress = modeProgress,
            UseBitmapRenderer = useBitmapRenderer,
            BackgroundMode = backgroundMode
        };

        control.Measure(new Size(size, size));
        control.Arrange(new Rect(0, 0, size, size));
        control.UpdateLayout();

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);

        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }

    private static (int Left, int Top, int Right, int Bottom) FindVisibleBounds(byte[] pixels, int size, byte alphaCutoff) {
        var left = size;
        var top = size;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < size; y++) {
            for (var x = 0; x < size; x++) {
                if (pixels[((y * size) + x) * 4 + 3] < alphaCutoff) {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top) {
            throw new InvalidOperationException("Expected rendered profile to contain visible pixels.");
        }

        return (left, top, right, bottom);
    }

    private static void AssertTrue(bool value, string message) {
        if (!value) {
            throw new InvalidOperationException(message);
        }
    }
}
