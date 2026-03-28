using System.Globalization;
using System.Text;

namespace NuraDesktopConsole.Library;

internal static class Hex {
    public static byte[] Parse(string value) {
        var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if ((compact.Length & 1) != 0) {
            throw new InvalidOperationException("hex string must have an even length");
        }

        var output = new byte[compact.Length / 2];
        for (var i = 0; i < output.Length; i++) {
            output[i] = byte.Parse(compact.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return output;
    }

    public static string Format(ReadOnlySpan<byte> bytes) {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) {
            _ = sb.AppendFormat(CultureInfo.InvariantCulture, "{0:x2}", b);
        }

        return sb.ToString();
    }
}
