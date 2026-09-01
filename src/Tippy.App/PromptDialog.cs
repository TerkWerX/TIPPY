using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tippy.App;

public static class PromptDialog
{
    public static string? Ask(Window owner, string title, string prompt, string initialValue)
    {
        var box = new TextBox { Text = initialValue, MinWidth = 330, Margin = new Thickness(0, 8, 0, 18) };
        var dialog = Create(owner, title, prompt, box);
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text : null;
    }

    public static string? Choose(Window owner, string title, string prompt, IReadOnlyList<string> choices)
    {
        var list = new ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 330, Margin = new Thickness(0, 8, 0, 18) };
        var dialog = Create(owner, title, prompt, list);
        return dialog.ShowDialog() == true ? list.SelectedItem?.ToString() : null;
    }

    private static Window Create(Window owner, string title, string prompt, Control input)
    {
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = 410, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Background = (Brush)Application.Current.FindResource("BackgroundBrush")
        };
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = prompt, FontSize = 16, FontWeight = FontWeights.SemiBold });
        root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var ok = new Button
        {
            Content = "OK", IsDefault = true, Background = (Brush)Application.Current.FindResource("AccentBrush"),
            Foreground = Brushes.Black
        };
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        dialog.Content = root;
        return dialog;
    }
}
