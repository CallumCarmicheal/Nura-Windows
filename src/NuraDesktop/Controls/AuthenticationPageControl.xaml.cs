using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using NuraDesktop.ViewModels;

namespace NuraDesktop.Controls;

public partial class AuthenticationPageControl : UserControl {
    private MainViewModel? _viewModel;
    private bool _isLoaded;

    public AuthenticationPageControl() {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        _isLoaded = true;
        AttachViewModel(DataContext as MainViewModel);
        UpdateStepVisual(immediate: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) {
        _isLoaded = false;
        AttachViewModel(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
        AttachViewModel(e.NewValue as MainViewModel);
        if (_isLoaded) {
            UpdateStepVisual(immediate: true);
        }
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
        if (e.PropertyName == nameof(MainViewModel.IsAuthenticationCodeStep)) {
            Dispatcher.BeginInvoke(
                () => UpdateStepVisual(immediate: false),
                DispatcherPriority.Loaded);
        }
    }
    private int _transitionGeneration;

    private void UpdateStepVisual(bool immediate) {
        var showCodeStep = _viewModel?.IsAuthenticationCodeStep == true;
        var generation = ++_transitionGeneration;

        if (immediate || !IsLoaded || AuthenticationCard.ActualHeight <= 0) {
            AuthenticationCard.BeginAnimation(HeightProperty, null);
            AuthenticationCard.ClearValue(HeightProperty);

            SetPanelState(EmailStepPanel, EmailStepTransform, !showCodeStep, visibleY: 0, hiddenY: -18);
            SetPanelState(CodeStepPanel, CodeStepTransform, showCodeStep, visibleY: 0, hiddenY: 18);

            FocusActiveInput(showCodeStep);
            return;
        }

        AnimateCardAndPanels(showCodeStep, generation);
    }

    private void AnimateCardAndPanels(bool showCodeStep, int generation) {
        var fromPanel = showCodeStep ? EmailStepPanel : CodeStepPanel;
        var fromTransform = showCodeStep ? EmailStepTransform : CodeStepTransform;

        var toPanel = showCodeStep ? CodeStepPanel : EmailStepPanel;
        var toTransform = showCodeStep ? CodeStepTransform : EmailStepTransform;

        var fromHiddenY = showCodeStep ? -18 : 18;
        var toHiddenY = showCodeStep ? 18 : -18;

        var fromHeight = AuthenticationCard.ActualHeight;
        var toHeight = MeasureCardHeightForPanel(toPanel);

        AuthenticationCard.BeginAnimation(HeightProperty, null);
        AuthenticationCard.Height = fromHeight;

        toPanel.Visibility = Visibility.Visible;
        toPanel.Opacity = 0;
        toTransform.Y = toHiddenY;

        var duration = TimeSpan.FromMilliseconds(260);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var heightAnimation = new DoubleAnimation {
            From = fromHeight,
            To = toHeight,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        heightAnimation.Completed += (_, _) => {
            if (generation != _transitionGeneration) {
                return;
            }

            AuthenticationCard.BeginAnimation(HeightProperty, null);
            AuthenticationCard.ClearValue(HeightProperty);

            fromPanel.Visibility = Visibility.Collapsed;

            FocusActiveInput(showCodeStep);
        };

        AuthenticationCard.BeginAnimation(HeightProperty, heightAnimation);

        AnimatePanelVisibility(
            fromPanel,
            fromTransform,
            show: false,
            hiddenY: fromHiddenY,
            generation);

        AnimatePanelVisibility(
            toPanel,
            toTransform,
            show: true,
            hiddenY: toHiddenY,
            generation);
    }

    private double MeasureCardHeightForPanel(FrameworkElement targetPanel) {
        var oldVisibility = targetPanel.Visibility;
        var oldOpacity = targetPanel.Opacity;

        targetPanel.Visibility = Visibility.Visible;
        targetPanel.Opacity = 0;

        var availableWidth = Math.Max(
            0,
            AuthenticationCard.ActualWidth
            - AuthenticationCard.Padding.Left
            - AuthenticationCard.Padding.Right
            - AuthenticationCard.BorderThickness.Left
            - AuthenticationCard.BorderThickness.Right);

        targetPanel.Measure(new Size(availableWidth, double.PositiveInfinity));

        var desiredHeight =
            targetPanel.DesiredSize.Height
            + AuthenticationCard.Padding.Top
            + AuthenticationCard.Padding.Bottom
            + AuthenticationCard.BorderThickness.Top
            + AuthenticationCard.BorderThickness.Bottom;

        targetPanel.Visibility = oldVisibility;
        targetPanel.Opacity = oldOpacity;

        return desiredHeight;
    }

    private static void SetPanelState(
        FrameworkElement panel,
        TranslateTransform transform,
        bool visible,
        double visibleY,
        double hiddenY) {
        panel.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);

        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        panel.Opacity = visible ? 1 : 0;
        transform.Y = visible ? visibleY : hiddenY;
    }

    private void AnimatePanelVisibility(
        FrameworkElement panel,
        TranslateTransform transform,
        bool show,
        double hiddenY,
        int generation) {
        panel.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);

        if (show) {
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0;
            transform.Y = hiddenY;
        }

        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase {
            EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn
        };

        var opacityAnimation = new DoubleAnimation {
            To = show ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = easing
        };

        if (!show) {
            opacityAnimation.Completed += (_, _) => {
                if (generation == _transitionGeneration) {
                    panel.Visibility = Visibility.Collapsed;
                }
            };
        }

        panel.BeginAnimation(OpacityProperty, opacityAnimation);

        transform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation {
                To = show ? 0.0 : hiddenY,
                Duration = duration,
                EasingFunction = easing
            });
    }

    private void FocusActiveInput(bool showCodeStep) {
        Dispatcher.BeginInvoke(() => {
            if (showCodeStep) {
                OtpCodeInput.FocusFirstEmptyBox();
                return;
            }

            EmailTextBox.Focus();
            EmailTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void EmailTextBox_OnKeyDown(object sender, KeyEventArgs e) {
        if (e.Key != Key.Enter) {
            return;
        }

        if (_viewModel?.SubmitAuthenticationEmailCommand.CanExecute(null) == true) {
            _viewModel.SubmitAuthenticationEmailCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OtpCodeInput_OnCodeCompleted(object? sender, EventArgs e) {
        if (_viewModel?.VerifyAuthenticationCodeCommand.CanExecute(null) == true) {
            _viewModel.VerifyAuthenticationCodeCommand.Execute(null);
        }
    }
}
