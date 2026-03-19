using System.Globalization;

namespace desktop_app.Transport;

internal static class BluetoothAddress {
    public static ulong Parse(string value) {
        var compact = value.Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (compact.Length != 12) {
            throw new InvalidOperationException("Bluetooth address must contain 6 bytes");
        }

        return ulong.Parse(compact, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
