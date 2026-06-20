using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NuraPopupWpf.Controls;

public class LabeledTickBar : TickBar {
    public static readonly DependencyProperty LabelsProperty =
        DependencyProperty.Register(
            nameof(Labels),
            typeof(string),
            typeof(LabeledTickBar),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Labels {
        get => (string)GetValue(LabelsProperty);
        set => SetValue(LabelsProperty, value);
    }

    protected override void OnRender(DrawingContext dc) {
        var labels = (Labels ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToArray();

        if (labels.Length == 0 || Maximum <= Minimum)
            return;

        double range = Maximum - Minimum;
        if (range <= 0)
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var typeface = new Typeface("Segoe UI");
        double fontSize = 11;

        for (int i = 0; i < labels.Length; i++) {
            double value = Minimum + i;

            double x = ReservedSpace + ((ActualWidth - (2 * ReservedSpace)) * (value - Minimum) / range);

            var text = new FormattedText(
                labels[i],
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Fill,
                dpi.PixelsPerDip);

            dc.DrawText(text, new Point(x - (text.Width / 2), 0));
        }
    }
}