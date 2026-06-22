using System;
using System.Collections.Generic;
using System.Text;

namespace NuraTerm;

public static class NuraGradient {
    public enum Gradient {
        PurpleBranding = 0,

        HearingProfileAll,
        HearingProfileOrangeToRed,
        HearingProfileRedToPink,
        HearingProfilePinkToPurple,
        HearingProfilePurpleToBlue,
        HearingProfileBlueToCyan,
        HearingProfileCyanToMint
    }

    private static readonly Rgb Start = Rgb.FromHex(0x804DC4);
    private static readonly Rgb Middle = Rgb.FromHex(0xE35CA9);
    private static readonly Rgb End = Rgb.FromHex(0xE07168);

    private static readonly GradientStop[] CurrentGradientStops = {
        new(0.00, Start),
        new(0.50, Middle),
        new(1.00, End),
    };

    private static readonly GradientStop[] HearingProfileAllGradientStops = {
        new(0.00, RgbFromRgb(240, 127, 87)),
        new(0.12, RgbFromRgb(236, 28, 36)),
        new(0.28, RgbFromRgb(238, 88, 149)),
        new(0.41, RgbFromRgb(160, 76, 156)),
        new(0.60, RgbFromRgb(61, 83, 163)),
        new(0.80, RgbFromRgb(8, 152, 205)),
        new(1.00, RgbFromRgb(141, 209, 199)),
    };

    private static readonly GradientStop[] HearingProfileOrangeToRed = {
        new(0.00, RgbFromRgb(240, 127, 87)),
        new(1.00, RgbFromRgb(236, 28, 36)),
    };

    private static readonly GradientStop[] HearingProfileRedToPink = {
        new(0.00, RgbFromRgb(236, 28, 36)),
        new(1.00, RgbFromRgb(238, 88, 149)),
    };

    private static readonly GradientStop[] HearingProfilePinkToPurple = {
        new(0.00, RgbFromRgb(238, 88, 149)),
        new(1.00, RgbFromRgb(160, 76, 156)),
    };

    private static readonly GradientStop[] HearingProfilePurpleToBlue = {
        new(0.00, RgbFromRgb(160, 76, 156)),
        new(1.00, RgbFromRgb(61, 83, 163)),
    };

    private static readonly GradientStop[] HearingProfileBlueToCyan = {
        new(0.00, RgbFromRgb(61, 83, 163)),
        new(1.00, RgbFromRgb(8, 152, 205)),
    };

    private static readonly GradientStop[] HearingProfileCyanToMint = {
        new(0.00, RgbFromRgb(8, 152, 205)),
        new(1.00, RgbFromRgb(141, 209, 199)),
    };

    public static AnsiPart[] Text(
        string text,
        bool colourWhitespace = false,
        Gradient gradient = Gradient.PurpleBranding) {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<AnsiPart>();

        GradientStop[] stops = GetStops(gradient);

        if (text.Length == 1) {
            return new[]
            {
                char.IsWhiteSpace(text[0]) && !colourWhitespace
                    ? new AnsiPart(text)
                    : new AnsiPart(text, new AnsiStyle(Foreground: stops[0].Colour))
            };
        }

        var parts = new AnsiPart[text.Length];

        for (int i = 0; i < text.Length; i++) {
            if (char.IsWhiteSpace(text[i]) && !colourWhitespace) {
                parts[i] = new AnsiPart(text[i].ToString());
                continue;
            }

            double t = i / (double)(text.Length - 1);
            Rgb colour = GetColourAt(stops, t);

            parts[i] = new AnsiPart(
                text[i].ToString(),
                new AnsiStyle(Foreground: colour));
        }

        return parts;
    }

    public static AnsiPart[] Text(
        string text,
        Gradient gradient,
        bool colourWhitespace = false) {
        return Text(text, colourWhitespace, gradient);
    }

    private static GradientStop[] GetStops(Gradient gradient) {
        return gradient switch {
            Gradient.PurpleBranding => CurrentGradientStops,
            Gradient.HearingProfileAll => HearingProfileAllGradientStops,
            Gradient.HearingProfileOrangeToRed => HearingProfileOrangeToRed,
            Gradient.HearingProfileRedToPink => HearingProfileRedToPink,
            Gradient.HearingProfilePinkToPurple => HearingProfilePinkToPurple,
            Gradient.HearingProfilePurpleToBlue => HearingProfilePurpleToBlue,
            Gradient.HearingProfileBlueToCyan => HearingProfileBlueToCyan,
            Gradient.HearingProfileCyanToMint => HearingProfileCyanToMint,
            _ => CurrentGradientStops,
        };
    }

    private static Rgb GetColourAt(GradientStop[] stops, double t) {
        t = Math.Clamp(t, 0, 1);

        if (t <= stops[0].Position)
            return stops[0].Colour;

        for (int i = 1; i < stops.Length; i++) {
            GradientStop previous = stops[i - 1];
            GradientStop current = stops[i];

            if (t <= current.Position) {
                double range = current.Position - previous.Position;
                double localT = range <= 0
                    ? 0
                    : (t - previous.Position) / range;

                return Lerp(previous.Colour, current.Colour, localT);
            }
        }

        return stops[^1].Colour;
    }

    private static Rgb Lerp(Rgb a, Rgb b, double t) {
        t = Math.Clamp(t, 0, 1);

        return new Rgb(
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    private static Rgb RgbFromRgb(int r, int g, int b) {
        return new Rgb((byte)r, (byte)g, (byte)b);
    }

    private readonly struct GradientStop {
        public GradientStop(double position, Rgb colour) {
            Position = position;
            Colour = colour;
        }

        public double Position { get; }
        public Rgb Colour { get; }
    }
}
