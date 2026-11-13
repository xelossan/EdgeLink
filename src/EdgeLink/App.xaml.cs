using System.Windows;
using EdgeLink.Services;

namespace EdgeLink;

public partial class App : Application {
    private const string InstanceName = "EdgeLink.SingleInstance";
    private const string ShowEventName = "EdgeLink.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, InstanceName, out var isFirstInstance);
        if (!isFirstInstance) {
            // already running: ask that instance to show its window instead
            try {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (Exception) {
                // the other instance runs elevated and is out of reach; nothing else to do
            }

            _mutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) => {
            Log.Write("Unhandled exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "EdgeLink", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _controller = new AppController();
        _controller.Start();
        _tray = new TrayIcon(_controller, ShowWindow, Shutdown);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var showEvent = _showEvent;
        new Thread(() => {
            try {
                while (showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
            }
            catch (ObjectDisposedException) {
                // shutting down
            }
        }) { IsBackground = true, Name = "EdgeLink activation" }.Start();

        // started by autostart: stay in the tray, unless there is nothing configured yet
        if (!e.Args.Contains("--tray") || _controller.IsFirstRun) ShowWindow();
    }

    private void ShowWindow()
    {
        if (_controller == null) return;

        if (_window == null) {
            _window = new MainWindow(_controller);
            _window.Closed += (_, _) => _window = null;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _controller?.Dispose();
        _showEvent?.Dispose();
        _mutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
