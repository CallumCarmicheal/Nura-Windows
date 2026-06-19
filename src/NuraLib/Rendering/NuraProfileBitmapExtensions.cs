using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using SDIPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace NuraLib.Rendering;

public static class NuraProfileBitmapExtensions {

    /// <summary>
    /// Converts a Nura profile bitmap into a System.Drawing bitmap.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static Bitmap ToDrawingBitmap(this NuraProfileBitmap source) {
        ArgumentNullException.ThrowIfNull(source);
        ValidateBitmap(source);

        var bitmap = new Bitmap(
            source.Width,
            source.Height,
            SDIPixelFormat.Format32bppArgb
        );

        var rect = new Rectangle(
            0,
            0,
            source.Width,
            source.Height
        );

        BitmapData data = bitmap.LockBits(
            rect,
            ImageLockMode.WriteOnly,
            SDIPixelFormat.Format32bppArgb
        );

        try {
            for (int y = 0; y < source.Height; y++) {
                int sourceOffset = y * source.Stride;

                IntPtr destinationRow = data.Stride > 0
                    ? data.Scan0 + (y * data.Stride)
                    : data.Scan0 + ((source.Height - 1 - y) * -data.Stride);

                Marshal.Copy(
                    source.Pixels,
                    sourceOffset,
                    destinationRow,
                    source.Stride
                );
            }
        } finally {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>
    /// Converts a Nura profile bitmap into a WPF BitmapSource.
    /// </summary>
    public static BitmapSource ToBitmapSource(
        this NuraProfileBitmap source,
        double dpiX = 96.0,
        double dpiY = 96.0,
        bool freeze = true
    ) {
        ArgumentNullException.ThrowIfNull(source);
        ValidateBitmap(source);

        BitmapSource bitmapSource = BitmapSource.Create(
            source.Width,
            source.Height,
            dpiX,
            dpiY,
            PixelFormats.Pbgra32,
            palette: null,
            pixels: source.Pixels,
            stride: source.Stride
        );

        if (freeze && bitmapSource.CanFreeze) {
            bitmapSource.Freeze();
        }

        return bitmapSource;
    }

    private static void ValidateBitmap(NuraProfileBitmap source) {
        if (source.Width <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                "Bitmap width must be greater than zero."
            );
        }

        if (source.Height <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                "Bitmap height must be greater than zero."
            );
        }

        int expectedStride = checked(source.Width * 4);
        int expectedLength = checked(expectedStride * source.Height);

        if (source.Stride != expectedStride) {
            throw new ArgumentException(
                "Bitmap stride must equal width * 4 for BGRA32 pixel data.",
                nameof(source)
            );
        }

        if (source.Pixels.Length < expectedLength) {
            throw new ArgumentException(
                "Bitmap pixel buffer is smaller than width * height * 4.",
                nameof(source)
            );
        }
    }
}
