using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using static DialShift.Desktop.MainWindow;

namespace DialShift.Desktop;

public static class MessageDialog
{
    public static Task Show(Window owner, string title, string text) => Open(owner, title, text, false);
    public static Task<bool> Confirm(Window owner, string title, string text) => Open(owner, title, text, true);
    private static Task<bool> Open(Window owner, string title, string text, bool confirm)
    {
        var dialog = new Window { Title = title, Width = 490, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#192527") };
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        content.Children.Add(Text(text));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (confirm) buttons.Children.Add(Button("Cancel", () => dialog.Close(false)));
        buttons.Children.Add(Button(confirm ? "Confirm" : "OK", () => dialog.Close(true), true));
        content.Children.Add(buttons); dialog.Content = content;
        return dialog.ShowDialog<bool>(owner);
    }
}
