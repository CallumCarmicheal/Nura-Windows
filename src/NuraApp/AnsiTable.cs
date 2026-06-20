using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NuraApp;

public static class AnsiTable {
    private static readonly Regex AnsiRegex =
        new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public static string Render(
        IReadOnlyList<string> columns,
        IReadOnlyList<string>? titles = null,
        bool verticalDivider = true,
        string divider = " | ") {
        if (columns.Count == 0)
            return string.Empty;

        titles ??= Enumerable.Range(1, columns.Count)
            .Select(i => $"Title {i}")
            .ToArray();

        var lines = columns
            .Select(c => SplitLines(c))
            .ToArray();

        var widths = new int[columns.Count];

        for (int i = 0; i < columns.Count; i++) {
            var title = i < titles.Count ? titles[i] : string.Empty;

            widths[i] = Math.Max(
                VisibleWidth(title),
                lines[i].DefaultIfEmpty("").Max(VisibleWidth)
            );
        }

        var sb = new StringBuilder();
        var joiner = verticalDivider ? divider : "  ";

        AppendRow(sb, titles.Select((t, i) => i < columns.Count ? t : ""), widths, joiner);

        AppendRow(
            sb,
            widths.Select(w => new string('─', Math.Max(1, w))),
            widths,
            joiner
        );

        var maxRows = lines.Max(x => x.Length);

        for (int row = 0; row < maxRows; row++) {
            var cells = new string[columns.Count];

            for (int col = 0; col < columns.Count; col++)
                cells[col] = row < lines[col].Length ? lines[col][row] : string.Empty;

            AppendRow(sb, cells, widths, joiner);
        }

        return sb.ToString();
    }

    public static string Render(params string[] columns) {
        return Render(columns, titles: null, verticalDivider: true);
    }

    private static void AppendRow(
        StringBuilder sb,
        IEnumerable<string> cells,
        int[] widths,
        string joiner) {
        var array = cells.ToArray();

        for (int i = 0; i < widths.Length; i++) {
            var cell = i < array.Length ? array[i] : string.Empty;
            sb.Append(PadRightAnsi(cell, widths[i]));

            if (i < widths.Length - 1)
                sb.Append(joiner);
        }

        sb.AppendLine();
    }

    private static string[] SplitLines(string text) {
        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static string PadRightAnsi(string text, int width) {
        var visible = VisibleWidth(text);
        return text + new string(' ', Math.Max(0, width - visible));
    }

    private static int VisibleWidth(string text) {
        text = AnsiRegex.Replace(text, "");

        var width = 0;

        foreach (var rune in text.EnumerateRunes()) {
            var category = CharUnicodeInfo.GetUnicodeCategory(rune.ToString(), 0);

            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format
                or UnicodeCategory.Control) {
                continue;
            }

            width += IsWide(rune.Value) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWide(int codepoint) {
        return
            codepoint >= 0x1100 &&
            (
                codepoint <= 0x115F ||
                codepoint == 0x2329 ||
                codepoint == 0x232A ||
                codepoint is >= 0x2E80 and <= 0xA4CF and not 0x303F ||
                codepoint is >= 0xAC00 and <= 0xD7A3 ||
                codepoint is >= 0xF900 and <= 0xFAFF ||
                codepoint is >= 0xFE10 and <= 0xFE19 ||
                codepoint is >= 0xFE30 and <= 0xFE6F ||
                codepoint is >= 0xFF00 and <= 0xFF60 ||
                codepoint is >= 0xFFE0 and <= 0xFFE6
            );
    }
}