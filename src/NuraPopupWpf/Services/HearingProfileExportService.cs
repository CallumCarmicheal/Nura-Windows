using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

using NuraPopupWpf.Controls;
using NuraPopupWpf.Models;

namespace NuraPopupWpf.Services;

public sealed class HearingProfileExportService {
    private const int MinRenderSize = 4;
    private const int MaxRenderSize = 12288;

    private readonly NuraProfileRenderer _bitmapRenderer = new();

    public string ExportProfiles(IEnumerable<DeviceModel> devices, int requestedSize, bool useBitmapRenderer) {
        var renderSize = Math.Clamp(requestedSize, MinRenderSize, MaxRenderSize);
        var exportDate = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var exportDirectory = Path.Combine(Environment.CurrentDirectory, "renders", exportDate);
        Directory.CreateDirectory(exportDirectory);

        foreach (var device in devices) {
            foreach (var profile in device.Profiles) {
                var fileName = $"{SanitizeFileName(device.Name)} - {SanitizeFileName(profile.Name)}.png";
                var filePath = Path.Combine(exportDirectory, fileName);
                var bitmap = useBitmapRenderer
                    ? _bitmapRenderer.Render(profile, profile, 1.0, 1.0, renderSize, 1.0)
                    : RenderShapeBitmap(profile, renderSize);

                SaveBitmap(filePath, bitmap);
            }
        }

        return exportDirectory;
    }

    private static BitmapSource RenderShapeBitmap(ProfileModel profile, int size) {
        var root = new Grid {
            Width = size,
            Height = size,
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true
        };

        var blurLayer = new ProfileVisualControl {
            Width = size,
            Height = size,
            FromProfile = profile,
            ToProfile = profile,
            ProfileBlendProgress = 1.0,
            ModeProgress = 1.0,
            ImmersionValue = 1.0,
            UseBitmapRenderer = false,
            IsMorphing = false,
            RenderShadow = false,
            Opacity = GetBlurOpacity(size),
            CacheMode = new BitmapCache { RenderAtScale = 0.8 },
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new BlurEffect {
                Radius = GetBlurRadius(size),
                RenderingBias = RenderingBias.Performance
            },
            RenderTransform = new ScaleTransform(1.01, 1.01)
        };

        var mainLayer = new ProfileVisualControl {
            Width = size,
            Height = size,
            FromProfile = profile,
            ToProfile = profile,
            ProfileBlendProgress = 1.0,
            ModeProgress = 1.0,
            ImmersionValue = 1.0,
            UseBitmapRenderer = false,
            IsMorphing = false,
            RenderShadow = true
        };

        root.Children.Add(blurLayer);
        root.Children.Add(mainLayer);

        root.Measure(new Size(size, size));
        root.Arrange(new Rect(0, 0, size, size));
        root.UpdateLayout();

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        bitmap.Freeze();
        return bitmap;
    }

    private static double GetBlurRadius(int size) => size <= 250 ? 8.0 : 10.0;

    private static double GetBlurOpacity(int size) => size <= 250 ? 0.26 : 0.28;

    private static void SaveBitmap(string filePath, BitmapSource bitmap) {
        using var stream = File.Create(filePath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static string SanitizeFileName(string value) {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return sanitized.Trim();
    }
}
