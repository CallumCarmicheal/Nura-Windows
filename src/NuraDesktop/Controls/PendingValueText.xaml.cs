using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NuraDesktop.Controls;

/// <summary>
/// Shows a confirmed value normally and a subtle shimmer while its device change is pending.
/// </summary>
public partial class PendingValueText : UserControl {
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PendingValueText), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsPendingProperty = DependencyProperty.Register(
        nameof(IsPending), typeof(bool), typeof(PendingValueText), new PropertyMetadata(false, OnPendingChanged));

    public PendingValueText() {
        InitializeComponent();
        Loaded += (_, _) => UpdatePendingVisual();
    }

    public string Text {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsPending {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    private static void OnPendingChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs _) {
        if (dependencyObject is PendingValueText control) {
            control.UpdatePendingVisual();
        }
    }

    private void UpdatePendingVisual() {
        if (!IsLoaded) {
            return;
        }

        ShimmerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ShimmerTransform.X = -1;
        ShimmerText.BeginAnimation(OpacityProperty, null);

        if (!IsPending) {
            ShimmerText.Opacity = 0;
            return;
        }

        ShimmerText.Opacity = 0.9;
        ShimmerTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation {
            From = -1,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(1050),
            RepeatBehavior = RepeatBehavior.Forever
        });
    }
}
