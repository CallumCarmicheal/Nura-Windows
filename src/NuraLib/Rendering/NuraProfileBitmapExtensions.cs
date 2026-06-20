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
        var pixels = source.IsPremultiplied ? Unpremultiply(source.Pixels) : source.Pixels;

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
                    pixels,
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
        var pixels = source.IsPremultiplied ? source.Pixels : Premultiply(source.Pixels);

        BitmapSource bitmapSource = BitmapSource.Create(
            source.Width,
            source.Height,
            dpiX,
            dpiY,
            PixelFormats.Pbgra32,
            palette: null,
            pixels: pixels,
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

    private static byte[] Premultiply(byte[] source) {
        var pixels = new byte[source.Length];

        for (var index = 0; index < source.Length; index += 4) {
            var alpha = source[index + 3];
            pixels[index] = Multiply(source[index], alpha);
            pixels[index + 1] = Multiply(source[index + 1], alpha);
            pixels[index + 2] = Multiply(source[index + 2], alpha);
            pixels[index + 3] = alpha;
        }

        return pixels;
    }

    private static byte[] Unpremultiply(byte[] source) {
        var pixels = new byte[source.Length];

        for (var index = 0; index < source.Length; index += 4) {
            var alpha = source[index + 3];
            pixels[index] = Unmultiply(source[index], alpha);
            pixels[index + 1] = Unmultiply(source[index + 1], alpha);
            pixels[index + 2] = Unmultiply(source[index + 2], alpha);
            pixels[index + 3] = alpha;
        }

        return pixels;
    }

    private static byte Multiply(byte colour, byte alpha) =>
        (byte)((colour * alpha + (byte.MaxValue / 2)) / byte.MaxValue);

    private static byte Unmultiply(byte colour, byte alpha) => alpha == 0
        ? (byte)0
        : (byte)Math.Min(byte.MaxValue, ((colour * byte.MaxValue) + (alpha / 2)) / alpha);
}
