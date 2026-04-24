using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace NuraPopupWpf.Controls;

public class AnimatedSlider : Slider {
    public static readonly DependencyProperty AnimatedValueProperty =
        DependencyProperty.Register(
            nameof(AnimatedValue),
            typeof(double),
            typeof(AnimatedSlider),
            new PropertyMetadata(0.0));

    public double AnimatedValue {
        get => (double)GetValue(AnimatedValueProperty);
        set => SetValue(AnimatedValueProperty, value);
    }

    public override void OnApplyTemplate() {
        base.OnApplyTemplate();
        BeginAnimation(AnimatedValueProperty, null);
        SetCurrentValue(AnimatedValueProperty, Value);
    }

    protected override void OnValueChanged(double oldValue, double newValue) {
        base.OnValueChanged(oldValue, newValue);

        if (!IsLoaded) {
            BeginAnimation(AnimatedValueProperty, null);
            SetCurrentValue(AnimatedValueProperty, newValue);
            return;
        }

        var animation = new DoubleAnimation {
            To = newValue,
            Duration = TimeSpan.FromMilliseconds(IsMouseCaptureWithin ? 90 : 160),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        BeginAnimation(AnimatedValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
