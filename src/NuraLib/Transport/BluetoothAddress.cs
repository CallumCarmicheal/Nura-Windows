using System.Globalization;

namespace NuraLib.Transport;

internal static class BluetoothAddress {
    public static ulong Parse(string value) {
        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (compact.Length != 12) {
            throw new InvalidOperationException("Bluetooth address must contain 6 bytes");
        }

        return ulong.Parse(compact, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static string Format(ulong value) {
        var compact = value.ToString("X12", CultureInfo.InvariantCulture);
        return string.Create(17, compact, static (buffer, source) => {
            var sourceIndex = 0;
            for (var i = 0; i < 6; i++) {
                buffer[(i * 3)] = source[sourceIndex++];
                buffer[(i * 3) + 1] = source[sourceIndex++];
                if (i < 5) {
                    buffer[(i * 3) + 2] = ':';
                }
            }
        });
    }
}
