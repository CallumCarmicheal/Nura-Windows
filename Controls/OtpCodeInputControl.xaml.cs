using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NuraPopupWpf.Controls;

public partial class OtpCodeInputControl : UserControl {
    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(
            nameof(Code),
            typeof(string),
            typeof(OtpCodeInputControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCodeChanged));

    private TextBox[]? _digitBoxes;
    private bool _isUpdatingBoxes;

    public OtpCodeInputControl() {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public event EventHandler? CodeCompleted;

    public string Code {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public void FocusFirstEmptyBox() {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        var target = _digitBoxes.FirstOrDefault(box => string.IsNullOrEmpty(box.Text)) ?? _digitBoxes[^1];
        target.Focus();
        target.SelectAll();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        EnsureDigitBoxes();
        ApplyCodeToBoxes(Code);
    }

    private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var control = (OtpCodeInputControl)d;
        control.ApplyCodeToBoxes(e.NewValue as string ?? string.Empty);
    }

    private void EnsureDigitBoxes() {
        _digitBoxes ??= [DigitBox0, DigitBox1, DigitBox2, DigitBox3, DigitBox4, DigitBox5];
    }

    private void DigitBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e) {
        var digits = NormaliseDigits(e.Text);
        if (digits.Length == 0) {
            e.Handled = true;
            return;
        }

        if (sender is not TextBox box) {
            e.Handled = true;
            return;
        }

        EnsureDigitBoxes();
        SetDigitsStartingAt(GetBoxIndex(box), digits);
        e.Handled = true;
    }

    private void DigitBox_OnPreviewKeyDown(object sender, KeyEventArgs e) {
        if (sender is not TextBox box) {
            return;
        }

        EnsureDigitBoxes();
        var index = GetBoxIndex(box);

        switch (e.Key) {
            case Key.Back:
                HandleBackspace(index);
                e.Handled = true;
                break;
            case Key.Delete:
                ClearBox(index);
                e.Handled = true;
                break;
            case Key.Left:
                FocusIndex(Math.Max(0, index - 1));
                e.Handled = true;
                break;
            case Key.Right:
                FocusIndex(Math.Min(5, index + 1));
                e.Handled = true;
                break;
            case Key.Space:
                e.Handled = true;
                break;
        }
    }

    private void DigitBox_OnTextChanged(object sender, TextChangedEventArgs e) {
        if (_isUpdatingBoxes || sender is not TextBox box) {
            return;
        }

        var digits = NormaliseDigits(box.Text);
        var index = GetBoxIndex(box);
        if (digits.Length == 0) {
            UpdateCodeFromBoxes();
            return;
        }

        SetDigitsStartingAt(index, digits);
    }

    private void DigitBox_OnPasting(object sender, DataObjectPastingEventArgs e) {
        if (!e.DataObject.GetDataPresent(typeof(string))) {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(typeof(string)) as string ?? string.Empty;
        var digits = NormaliseDigits(text);
        if (digits.Length == 0) {
            e.CancelCommand();
            return;
        }

        EnsureDigitBoxes();
        if (sender is not TextBox box) {
            e.CancelCommand();
            return;
        }

        SetDigitsStartingAt(GetBoxIndex(box), digits);
        e.CancelCommand();
    }

    private void SetDigitsStartingAt(int startIndex, string digits) {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        _isUpdatingBoxes = true;
        try {
            var index = startIndex;
            foreach (var digit in digits) {
                if (index >= _digitBoxes.Length) {
                    break;
                }

                _digitBoxes[index].Text = digit.ToString();
                index++;
            }
        } finally {
            _isUpdatingBoxes = false;
        }

        UpdateCodeFromBoxes();
        FocusIndex(Math.Min(5, startIndex + digits.Length));
    }

    private void HandleBackspace(int index) {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        if (!string.IsNullOrEmpty(_digitBoxes[index].Text)) {
            ClearBox(index);
            return;
        }

        var previousIndex = Math.Max(0, index - 1);
        ClearBox(previousIndex);
        FocusIndex(previousIndex);
    }

    private void ClearBox(int index) {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        _isUpdatingBoxes = true;
        try {
            _digitBoxes[index].Text = string.Empty;
        } finally {
            _isUpdatingBoxes = false;
        }

        UpdateCodeFromBoxes();
    }

    private void FocusIndex(int index) {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        var safeIndex = Math.Clamp(index, 0, _digitBoxes.Length - 1);
        _digitBoxes[safeIndex].Focus();
        _digitBoxes[safeIndex].SelectAll();
    }

    private void ApplyCodeToBoxes(string code) {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        var digits = NormaliseDigits(code);
        _isUpdatingBoxes = true;
        try {
            for (var i = 0; i < _digitBoxes.Length; i++) {
                _digitBoxes[i].Text = i < digits.Length ? digits[i].ToString() : string.Empty;
            }
        } finally {
            _isUpdatingBoxes = false;
        }
    }

    private void UpdateCodeFromBoxes() {
        EnsureDigitBoxes();
        if (_digitBoxes is null) {
            return;
        }

        var code = string.Concat(_digitBoxes.Select(box => box.Text));
        SetCurrentValue(CodeProperty, code);
        if (code.Length == _digitBoxes.Length && code.All(char.IsDigit)) {
            CodeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private int GetBoxIndex(TextBox box) {
        EnsureDigitBoxes();
        return _digitBoxes is null ? 0 : Array.IndexOf(_digitBoxes, box);
    }

    private static string NormaliseDigits(string? value) {
        return new string((value ?? string.Empty).Where(char.IsDigit).Take(6).ToArray());
    }
}
