using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Controls;

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

    private void UpdateStepVisual(bool immediate) {
        var showCodeStep = _viewModel?.IsAuthenticationCodeStep == true;
        if (immediate) {
            SetPanelState(EmailStepPanel, EmailStepTransform, !showCodeStep, visibleY: 0, hiddenY: -18);
            SetPanelState(CodeStepPanel, CodeStepTransform, showCodeStep, visibleY: 0, hiddenY: 18);
        } else {
            AnimatePanel(EmailStepPanel, EmailStepTransform, !showCodeStep, hiddenY: -18);
            AnimatePanel(CodeStepPanel, CodeStepTransform, showCodeStep, hiddenY: 18);
        }

        FocusActiveInput(showCodeStep);
    }

    private static void SetPanelState(FrameworkElement panel, TranslateTransform transform, bool visible, double visibleY, double hiddenY) {
        panel.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        panel.Opacity = visible ? 1 : 0;
        transform.Y = visible ? visibleY : hiddenY;
    }

    private static void AnimatePanel(FrameworkElement panel, TranslateTransform transform, bool show, double hiddenY) {
        panel.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);

        if (show) {
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0;
            transform.Y = hiddenY;
        }

        var duration = TimeSpan.FromMilliseconds(260);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacityAnimation = new DoubleAnimation {
            To = show ? 1.0 : 0.0,
            Duration = duration,
            EasingFunction = easing
        };

        if (!show) {
            opacityAnimation.Completed += (_, _) => panel.Visibility = Visibility.Collapsed;
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
