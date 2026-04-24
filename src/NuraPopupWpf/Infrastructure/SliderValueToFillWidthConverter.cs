using System;
using System.Globalization;
using System.Windows.Data;

namespace NuraPopupWpf.Infrastructure;

public sealed class SliderValueToFillWidthConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
        if (values.Length < 5) {
            return 0.0;
        }

        var value = ToDouble(values[0]);
        var minimum = ToDouble(values[1]);
        var maximum = ToDouble(values[2]);
        var trackWidth = ToDouble(values[3]);
        var thumbWidth = ToDouble(values[4]);

        if (maximum <= minimum || trackWidth <= 0) {
            return 0.0;
        }

        var progress = Math.Clamp((value - minimum) / (maximum - minimum), 0.0, 1.0);
        var travel = Math.Max(0.0, trackWidth - thumbWidth);
        return progress * travel + (thumbWidth / 2.0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static double ToDouble(object value) => value switch {
        double d => d,
        float f => f,
        int i => i,
        long l => l,
        _ => 0.0
    };
}
