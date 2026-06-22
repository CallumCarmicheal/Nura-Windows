using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using NuraLib.Rendering;

using NuraDesktop.Controls;
using NuraDesktop.Models;

namespace NuraDesktop.Services;

public sealed class HearingProfileExportService {
    private const int MinRenderSize = 4;
    private const int MaxRenderSize = 12288;


    public string ExportProfiles(IEnumerable<DeviceModel> devices, int requestedSize, bool useBitmapRenderer) {
        var renderSize = Math.Clamp(requestedSize, MinRenderSize, MaxRenderSize);
        var exportDate = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var exportDirectory = Path.Combine(Environment.CurrentDirectory, "renders", exportDate);
        Directory.CreateDirectory(exportDirectory);

        void render(bool transparent) {
            foreach (var device in devices) {
                foreach (var profile in device.Profiles) {
                    var fileName = $"{SanitizeFileName(device.Name)} - {SanitizeFileName(profile.Name)}{(transparent ? ".transparent" : "")}.png";
                    var filePath = Path.Combine(exportDirectory, fileName);

                    BitmapSource bitmap = null!;

                    if (useBitmapRenderer) {
                        bitmap = NuraProfileBitmapRenderer.Render(profile.VisualisationData, 1.0, renderSize, useTransparency: transparent).ToBitmapSource();
                    } else {
                        bitmap = RenderShapeBitmap(profile, renderSize, transparent);
                    }

                    SaveBitmap(filePath, bitmap);
                }
            }
        }
        
        // Render both transparent and white background.
        render(transparent: false);
        render(transparent: true);

        return exportDirectory;
    }

    private static BitmapSource RenderShapeBitmap(ProfileModel profile, int size, bool transparent) {
        var visual = new ProfileVisualControl {
            Width = size,
            Height = size,
            FromProfile = profile,
            ToProfile = profile,
            ProfileBlendProgress = 1.0,
            ModeProgress = 1.0,
            UseBitmapRenderer = false,
            BackgroundMode = transparent
                ? ProfileVisualBackgroundMode.Transparent
                : ProfileVisualBackgroundMode.White
        };

        visual.Measure(new Size(size, size));
        visual.Arrange(new Rect(0, 0, size, size));
        visual.UpdateLayout();

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

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
