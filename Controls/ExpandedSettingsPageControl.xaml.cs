using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NuraPopupWpf.Controls;

public partial class ExpandedSettingsPageControl : UserControl {
    public ExpandedSettingsPageControl() {
        InitializeComponent();
    }

#region Number Only Text Field
    private static readonly Regex _nonDigitRegex = new("[^0-9]+");

    private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) {
        e.Handled = _nonDigitRegex.IsMatch(e.Text);
    }

    private void NumberOnly_Pasting(object sender, DataObjectPastingEventArgs e) {
        if (!e.DataObject.GetDataPresent(typeof(string))) {
            e.CancelCommand();
            return;
        }

        var text = (string)e.DataObject.GetData(typeof(string));
        if (_nonDigitRegex.IsMatch(text)) {
            e.CancelCommand();
        }
    }
#endregion
}
