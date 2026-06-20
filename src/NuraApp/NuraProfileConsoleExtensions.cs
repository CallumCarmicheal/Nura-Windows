using System;
using System.Collections.Generic;
using System.Text;

namespace NuraApp;

using NuraLib.Rendering;

using System;
using System.Text;

public static class NuraProfileConsoleExtensions {

    public static NuraProfileBitmap TrimToVisibleSquare(
        this NuraProfileBitmap source,
        int padding = 0,
        byte alphaCutoff = 1,
        byte colourCutoff = 1
    ) {
        ArgumentNullException.ThrowIfNull(source);

        if (padding < 0) {
            throw new ArgumentOutOfRangeException(nameof(padding), "Padding cannot be negative.");
        }

        ValidateBitmap(source);

        int minX = source.Width;
        int minY = source.Height;
        int maxX = -1;
        int maxY = -1;

        int stride = source.Stride;

        for (int y = 0; y < source.Height; y++) {
            for (int x = 0; x < source.Width; x++) {
                int index = (y * stride) + (x * 4);

                byte b = source.Pixels[index + 0];
                byte g = source.Pixels[index + 1];
                byte r = source.Pixels[index + 2];
                byte a = source.Pixels[index + 3];

                if (!IsVisible(r, g, b, a, alphaCutoff, colourCutoff)) {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        // No visible pixels: return a tiny transparent square.
        if (maxX < minX || maxY < minY) {
            int emptySize = Math.Max(1, padding * 2);
            return new NuraProfileBitmap(
                emptySize,
                emptySize,
                new byte[emptySize * emptySize * 4]
            ) { IsPremultiplied = source.IsPremultiplied };
        }

        int contentWidth = maxX - minX + 1;
        int contentHeight = maxY - minY + 1;

        int squareSize = Math.Max(contentWidth, contentHeight);

        // Centre the visible content inside the square crop.
        int cropX = minX - ((squareSize - contentWidth) / 2);
        int cropY = minY - ((squareSize - contentHeight) / 2);

        int outputSize = squareSize + (padding * 2);
        byte[] outputPixels = new byte[outputSize * outputSize * 4];

        int sourceStartX = cropX - padding;
        int sourceStartY = cropY - padding;
        int outputStride = outputSize * 4;

        for (int y = 0; y < outputSize; y++) {
            int sourceY = sourceStartY + y;

            if (sourceY < 0 || sourceY >= source.Height) {
                continue;
            }

            for (int x = 0; x < outputSize; x++) {
                int sourceX = sourceStartX + x;

                if (sourceX < 0 || sourceX >= source.Width) {
                    continue;
                }

                int sourceIndex = (sourceY * source.Stride) + (sourceX * 4);
                int outputIndex = (y * outputStride) + (x * 4);

                outputPixels[outputIndex + 0] = source.Pixels[sourceIndex + 0]; // B
                outputPixels[outputIndex + 1] = source.Pixels[sourceIndex + 1]; // G
                outputPixels[outputIndex + 2] = source.Pixels[sourceIndex + 2]; // R
                outputPixels[outputIndex + 3] = source.Pixels[sourceIndex + 3]; // A
            }
        }

        return new NuraProfileBitmap(
            outputSize,
            outputSize,
            outputPixels
        ) { IsPremultiplied = source.IsPremultiplied };
    }

    private static bool IsVisible(
        byte r,
        byte g,
        byte b,
        byte a,
        byte alphaCutoff,
        byte colourCutoff
    ) {
        if (a < alphaCutoff) {
            return false;
        }

        int maxColour = Math.Max(r, Math.Max(g, b));

        return maxColour >= colourCutoff;
    }

    private static void ValidateBitmap(NuraProfileBitmap source) {
        if (source.Width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(source), "Bitmap width must be greater than zero.");
        }

        if (source.Height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(source), "Bitmap height must be greater than zero.");
        }

        int requiredLength = source.Stride * source.Height;

        if (source.Pixels.Length < requiredLength) {
            throw new ArgumentException("Pixel buffer is smaller than expected.", nameof(source));
        }
    }

    public static string BuildAnsiConsoleString(this NuraProfileBitmap source, byte alphaCutoff = 1) {
        ArgumentNullException.ThrowIfNull(source);

        int stride = source.Stride;

        if (source.Pixels.Length < stride * source.Height) {
            throw new ArgumentException("Pixel buffer is smaller than expected.", nameof(source));
        }

        var builder = new StringBuilder();

        for (int y = 0; y < source.Height; y += 2) {
            for (int x = 0; x < source.Width; x++) {
                Pixel top = ReadBgra(source.Pixels, (y * stride) + (x * 4));

                Pixel bottom = y + 1 < source.Height
                    ? ReadBgra(source.Pixels, ((y + 1) * stride) + (x * 4))
                    : Pixel.Transparent;

                bool topVisible = top.A >= alphaCutoff;
                bool bottomVisible = bottom.A >= alphaCutoff;

                if (topVisible && bottomVisible) {
                    builder.Append(
                        $"\x1b[38;2;{top.R};{top.G};{top.B}m" +
                        $"\x1b[48;2;{bottom.R};{bottom.G};{bottom.B}m" +
                        "▀"
                    );
                } else if (topVisible) {
                    builder.Append(
                        $"\x1b[0m" +
                        $"\x1b[38;2;{top.R};{top.G};{top.B}m" +
                        "▀"
                    );
                } else if (bottomVisible) {
                    builder.Append(
                        $"\x1b[0m" +
                        $"\x1b[38;2;{bottom.R};{bottom.G};{bottom.B}m" +
                        "▄"
                    );
                } else {
                    // Truly empty cell
                    builder.Append("\x1b[0m ");
                }
            }

            builder.Append("\x1b[0m");
            builder.AppendLine();
        }

        builder.Append("\x1b[0m");
        return builder.ToString();
    }

    private static Pixel ReadBgra(byte[] pixels, int index) {
        return new Pixel(
            R: pixels[index + 2],
            G: pixels[index + 1],
            B: pixels[index + 0],
            A: pixels[index + 3]
        );
    }

    private readonly record struct Pixel(byte R, byte G, byte B, byte A) {
        public static readonly Pixel Transparent = new(0, 0, 0, 0);
    }
}
