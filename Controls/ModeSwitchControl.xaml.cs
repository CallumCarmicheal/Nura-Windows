using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Controls;

public partial class ModeSwitchControl : UserControl {
    private MainViewModel? _viewModel;
    private const double PillInset = 2.0;

    private static readonly Color PersonalisedStart = (Color)ColorConverter.ConvertFromString("#FF8A64FF");
    private static readonly Color PersonalisedMid = (Color)ColorConverter.ConvertFromString("#FFE14FE2");
    private static readonly Color PersonalisedEnd = (Color)ColorConverter.ConvertFromString("#FFFF5AB2");
    private static readonly Color NeutralColor = (Color)ColorConverter.ConvertFromString("#7273A1");

    public ModeSwitchControl() {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => AnimatePill(immediate: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        AttachViewModel(DataContext as MainViewModel);
        AnimatePill(immediate: true);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        AttachViewModel(e.NewValue as MainViewModel);
        AnimatePill(immediate: true);
    }

    private void AttachViewModel(MainViewModel? next) {
        if (_viewModel is not null) {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = next;

        if (_viewModel is not null) {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(MainViewModel.IsPersonalised)) {
            AnimatePill(immediate: false);
        }
    }

    private void AnimatePill(bool immediate) {
        if (!IsLoaded || LayoutRoot.ActualWidth <= 0)
            return;

        var isPersonalised = _viewModel?.IsPersonalised == true;

        var baseWidth = Math.Max(0, (LayoutRoot.ActualWidth / 2.0) - (PillInset * 2.0));
        var targetX = isPersonalised ? LayoutRoot.ActualWidth / 2.0 : 0.0;

        if (immediate) {
            SlidingPill.BeginAnimation(WidthProperty, null);
            PillTransform.BeginAnimation(TranslateTransform.XProperty, null);

            SlidingPill.Width = baseWidth;
            PillTransform.X = targetX;
            ApplyPillColorsImmediately(isPersonalised);
            return;
        }

        var currentX = PillTransform.X;
        var delta = targetX - currentX;

        if (Math.Abs(delta) < 0.5) {
            SlidingPill.BeginAnimation(WidthProperty, null);
            PillTransform.BeginAnimation(TranslateTransform.XProperty, null);

            SlidingPill.Width = baseWidth;
            PillTransform.X = targetX;
            AnimatePillColorsDirectional(isPersonalised);
            return;
        }

        var stretchWidth = baseWidth + Math.Abs(delta);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var tailSnap = new CubicEase { EasingMode = EasingMode.EaseIn };

        var widthAnimation = new DoubleAnimationUsingKeyFrames();
        var xAnimation = new DoubleAnimationUsingKeyFrames();

        if (delta > 0) {
            // Moving right:
            // Hold left edge, stretch right, then move across and shrink back.
            widthAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(stretchWidth, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))) {
                    EasingFunction = easeOut
                });

            widthAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(baseWidth, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))) {
                    EasingFunction = tailSnap
                });

            xAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(currentX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));

            xAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(targetX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))) {
                    EasingFunction = tailSnap
                });
        } else {
            // Moving left:
            // Jump left edge first, stretch back to the left, then shrink in at destination.
            widthAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(stretchWidth, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))) {
                    EasingFunction = easeOut
                });

            widthAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(baseWidth, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))) {
                    EasingFunction = tailSnap
                });

            xAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(targetX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))) {
                    EasingFunction = easeOut
                });

            xAnimation.KeyFrames.Add(
                new EasingDoubleKeyFrame(targetX, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));
        }

        SlidingPill.BeginAnimation(WidthProperty, widthAnimation);
        PillTransform.BeginAnimation(TranslateTransform.XProperty, xAnimation);

        AnimatePillColorsDirectional(isPersonalised);
    }

    private void ApplyPillColorsImmediately(bool isPersonalised) {
        PillStop1.BeginAnimation(GradientStop.ColorProperty, null);
        PillStop2.BeginAnimation(GradientStop.ColorProperty, null);
        PillStop3.BeginAnimation(GradientStop.ColorProperty, null);

        if (isPersonalised) {
            PillStop1.Color = PersonalisedStart;
            PillStop2.Color = PersonalisedMid;
            PillStop3.Color = PersonalisedEnd;
        } else {
            PillStop1.Color = NeutralColor;
            PillStop2.Color = NeutralColor;
            PillStop3.Color = NeutralColor;
        }
    }

    private void AnimatePillColorsDirectional(bool isPersonalised) {
        var total = TimeSpan.FromMilliseconds(240);
        var mid = TimeSpan.FromMilliseconds(120);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

        if (!isPersonalised) {
            // Personalised -> Neutral
            // Grey appears in the middle first, then spreads outward.
            AnimateColorKeyframes(
                PillStop1,
                (PersonalisedStart, TimeSpan.Zero, null),
                (PersonalisedStart, mid, null),
                (NeutralColor, total, easeInOut));

            AnimateColorKeyframes(
                PillStop2,
                (PersonalisedMid, TimeSpan.Zero, null),
                (NeutralColor, mid, easeOut),
                (NeutralColor, total, null));

            AnimateColorKeyframes(
                PillStop3,
                (PersonalisedEnd, TimeSpan.Zero, null),
                (PersonalisedEnd, mid, null),
                (NeutralColor, total, easeInOut));
        } else {
            // Neutral -> Personalised
            // Colour comes back through the middle first, then spreads out.
            AnimateColorKeyframes(
                PillStop1,
                (NeutralColor, TimeSpan.Zero, null),
                (NeutralColor, mid, null),
                (PersonalisedStart, total, easeInOut));

            AnimateColorKeyframes(
                PillStop2,
                (NeutralColor, TimeSpan.Zero, null),
                (PersonalisedMid, mid, easeOut),
                (PersonalisedMid, total, null));

            AnimateColorKeyframes(
                PillStop3,
                (NeutralColor, TimeSpan.Zero, null),
                (NeutralColor, mid, null),
                (PersonalisedEnd, total, easeInOut));
        }
    }

    private static void AnimateColorKeyframes(
        GradientStop stop,
        params (Color color, TimeSpan time, IEasingFunction? easing)[] frames) {

        var animation = new ColorAnimationUsingKeyFrames();

        foreach (var frame in frames) {
            animation.KeyFrames.Add(
                new EasingColorKeyFrame(frame.color, KeyTime.FromTimeSpan(frame.time)) {
                    EasingFunction = frame.easing
                });
        }

        stop.BeginAnimation(GradientStop.ColorProperty, animation);
    }
}