using System.Windows;
using WinForms = System.Windows.Forms;

namespace EdgeLink.Services;

internal sealed class TrayIcon : IDisposable {
    private readonly AppController _controller;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _enabledItem;

    public TrayIcon(AppController controller, Action showWindow, Action exit)
    {
        _controller = controller;

        _enabledItem = new WinForms.ToolStripMenuItem("Włączony") { CheckOnClick = true, ShortcutKeyDisplayString = AppController.HotkeyText };
        _enabledItem.Click += (_, _) => controller.SetEnabled(_enabledItem.Checked);

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Otwórz EdgeLink", null, (_, _) => showWindow());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Zakończ", null, (_, _) => exit());

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/EdgeLink.ico"))!.Stream;
        _icon = new WinForms.NotifyIcon {
            Icon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) => {
            if (e.Button == WinForms.MouseButtons.Left) showWindow();
        };

        controller.Changed += Update;
        Update();
    }

    private void Update()
    {
        var enabled = _controller.Config.Settings.Enabled;
        _enabledItem.Checked = enabled;
        _icon.Text = enabled ? "EdgeLink" : "EdgeLink (wstrzymany)";
    }

    public void Dispose()
    {
        _controller.Changed -= Update;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
