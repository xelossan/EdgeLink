using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace EdgeLink.Services;

/// <summary>
/// The notification-area icon. Only the icon itself comes from WinForms; its menu is a WPF
/// context menu, so it follows the same Fluent theme as the rest of the application.
/// </summary>
internal sealed class TrayIcon : IDisposable {
    private readonly AppController _controller;
    private readonly WinForms.NotifyIcon _icon;
    private readonly ContextMenu _menu;
    private readonly MenuItem _enabledItem;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    public TrayIcon(AppController controller, Action showWindow, Action exit)
    {
        _controller = controller;

        var open = new MenuItem { Header = "Open EdgeLink", FontWeight = FontWeights.SemiBold };
        open.Click += (_, _) => showWindow();

        _enabledItem = new MenuItem { Header = "Enabled", IsCheckable = true, InputGestureText = AppController.HotkeyText };
        _enabledItem.Click += (_, _) => controller.SetEnabled(_enabledItem.IsChecked);

        var quit = new MenuItem { Header = "Exit" };
        quit.Click += (_, _) => exit();

        _menu = new ContextMenu { Placement = PlacementMode.MousePoint, Items = { open, _enabledItem, new Separator(), quit } };
        _menu.Opened += (_, _) => {
            // a popup of a background application does not close when the user clicks elsewhere
            // unless its window is the foreground one
            if (PresentationSource.FromVisual(_menu) is HwndSource source) SetForegroundWindow(source.Handle);
        };

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/EdgeLink.ico"))!.Stream;
        _icon = new WinForms.NotifyIcon {
            Icon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize),
            Visible = true
        };
        _icon.MouseUp += (_, e) => {
            if (e.Button == WinForms.MouseButtons.Left) showWindow();
            else if (e.Button == WinForms.MouseButtons.Right) _menu.IsOpen = true;
        };

        controller.Changed += Update;
        Update();
    }

    private void Update()
    {
        var enabled = _controller.Config.Settings.Enabled;
        _enabledItem.IsChecked = enabled;
        _icon.Text = enabled ? "EdgeLink" : "EdgeLink (paused)";
    }

    public void Dispose()
    {
        _controller.Changed -= Update;
        _menu.IsOpen = false;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
