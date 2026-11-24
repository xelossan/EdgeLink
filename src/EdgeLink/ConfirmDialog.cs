using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EdgeLink;

/// <summary>A small yes/no question in the application's own (Fluent) look, unlike the system message box.</summary>
internal sealed class ConfirmDialog : Window {
    private ConfirmDialog(string heading, string message, string confirmText)
    {
        Title = "EdgeLink";
        SizeToContent = SizeToContent.Height;
        Width = 440;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var confirm = new Button { Content = confirmText, IsDefault = true, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        if (TryFindResource("AccentButtonStyle") is Style accent) confirm.Style = accent;
        confirm.Click += (_, _) => DialogResult = true;

        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 96 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        content.Children.Add(new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
        content.Children.Add(buttons);
        Content = content;

        Loaded += (_, _) => Keyboard.Focus(confirm);
    }

    public static bool Ask(Window owner, string heading, string message, string confirmText) =>
        new ConfirmDialog(heading, message, confirmText) { Owner = owner }.ShowDialog() == true;
}
