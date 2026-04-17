using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Controls;

using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        DataContext = new MainViewModel();

    }

#region Titlebar Dragging
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState != MouseButtonState.Pressed) {
            return;
        }

        if (e.OriginalSource is DependencyObject source &&
            IsInteractiveElement(source)) {
            return;
        }

        DragMove();
    }

    private void ShellBackground_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ButtonState != MouseButtonState.Pressed || e.Handled) {
            return;
        }

        if (e.OriginalSource is not DependencyObject source || IsInteractiveElement(source)) {
            return;
        }

        DragMove();
        e.Handled = true;
    }

    private static bool IsInteractiveElement(DependencyObject source) {
        return FindAncestor<ButtonBase>(source) is not null ||
               FindAncestor<Selector>(source) is not null ||
               FindAncestor<ScrollBar>(source) is not null ||
               FindAncestor<Thumb>(source) is not null ||
               FindAncestor<Slider>(source) is not null ||
               FindAncestor<TextBox>(source) is not null ||
               FindAncestor<PasswordBox>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject {
        var parent = current;

        while (parent is not null) {
            if (parent is T match) {
                return match;
            }

            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
#endregion
}
