using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EdgeLink.Core;
using EdgeLink.Native;
using EdgeLink.Services;

namespace EdgeLink;

/// <summary>
/// Briefly shows, on every connected screen, the name EdgeLink knows it by, so the user can see
/// which physical display is which.
/// </summary>
internal sealed class IdentifyOverlay : Window {
    private static readonly List<IdentifyOverlay> Open = [];
    private static DispatcherTimer? _timer;

    private readonly PixelRect _bounds;

    private IdentifyOverlay(OnlineMonitor monitor)
    {
        _bounds = monitor.Bounds;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 480;
        Height = 190;

        var text = new StackPanel { Margin = new Thickness(28, 18, 28, 18) };
        text.Children.Add(new TextBlock {
            Text = monitor.Profile.Label, FontSize = 44, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        text.Children.Add(new TextBlock {
            Text = $"{monitor.Bounds.Width} × {monitor.Bounds.Height}", FontSize = 20, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xC4, 0xD0)),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        Content = new Border {
            Background = new SolidColorBrush(Color.FromRgb(0x12, 0x3A, 0x5E)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)),
            BorderThickness = new Thickness(3),
            Child = new Viewbox { Child = text }
        };

        SourceInitialized += (_, _) => Place();
        Loaded += (_, _) => Place();
    }

    /// <summary>Positions the window in physical pixels; WPF's own coordinates are unreliable across monitors with different scaling.</summary>
    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

        var width = Math.Min(_bounds.Width * 2 / 5, 760);
        var height = width * 2 / 5;
        NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST,
            _bounds.X + (_bounds.Width - width) / 2, _bounds.Y + (_bounds.Height - height) / 2, width, height,
            NativeMethods.SWP_NOACTIVATE);
    }

    public static void ShowAll(AppController controller)
    {
        CloseAll();

        foreach (var monitor in controller.Online) {
            var overlay = new IdentifyOverlay(monitor);
            Open.Add(overlay);
            overlay.Show();
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => CloseAll();
        _timer.Start();
    }

    private static void CloseAll()
    {
        _timer?.Stop();
        _timer = null;

        foreach (var overlay in Open) overlay.Close();
        Open.Clear();
    }
}
