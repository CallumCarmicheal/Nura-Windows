using System;
using System.Globalization;
using System.Windows.Data;

namespace NuraPopupWpf.Infrastructure;

public sealed class SliderValueToOffsetConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
        if (values.Length < 5) {
            return 0.0;
        }

        var value = ToDouble(values[0]);
        var minimum = ToDouble(values[1]);
        var maximum = ToDouble(values[2]);
        var trackWidth = ToDouble(values[3]);
        var thumbWidth = ToDouble(values[4]);

        if (maximum <= minimum || trackWidth <= 0 || thumbWidth <= 0) {
            return 0.0;
        }

        var progress = Math.Clamp((value - minimum) / (maximum - minimum), 0.0, 1.0);
        return progress * Math.Max(0.0, trackWidth - thumbWidth);
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
