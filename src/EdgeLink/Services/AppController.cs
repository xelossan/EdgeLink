using System.IO;
using System.Security.Principal;
using System.Windows.Threading;
using EdgeLink.Core;
using EdgeLink.Native;
using Microsoft.Win32;

namespace EdgeLink.Services;

/// <summary>
/// Owns the configuration, the list of connected monitors and the mouse hook, and keeps the three
/// in step. Lives on the UI thread.
/// </summary>
internal sealed class AppController : IDisposable {
    public const string HotkeyText = "Ctrl+Alt+Shift+E";

    /// <summary>The version of the running build, without the commit suffix the SDK appends.</summary>
    public static readonly string Version =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "").Split('+')[0];

    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeLink");

    private readonly ConfigStore _store = new(Path.Combine(DataDirectory, "config.json"));
    private readonly CrossingEngine _engine = new();
    private readonly MouseHook _hook;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _refreshTimer;

    public AppController()
    {
        IsFirstRun = !_store.Exists;
        Config = _store.Load();
        _hook = new MouseHook(_engine);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => SaveNow();

        // connecting a display fires a burst of change notifications; wait for it to settle
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _refreshTimer.Tick += (_, _) => {
            _refreshTimer.Stop();
            RefreshMonitors();
        };
    }

    public AppConfig Config { get; }
    public IReadOnlyList<OnlineMonitor> Online { get; private set; } = [];
    public bool IsFirstRun { get; }
    public bool HotkeyAvailable => _hook.HotkeyRegistered;

    public static bool IsElevated {
        get {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Raised after anything the UI shows has changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        RefreshMonitors();

        _hook.HotkeyPressed += () => _dispatcher.BeginInvoke(() => SetEnabled(!Config.Settings.Enabled));
        _hook.Enabled = Config.Settings.Enabled;
        _hook.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public OnlineMonitor? FindOnline(Guid monitorId) => Online.FirstOrDefault(o => o.Profile.Id == monitorId);

    public IEnumerable<MonitorProfile> OfflineProfiles => Config.Monitors.Where(m => FindOnline(m.Id) == null);

    /// <summary>Re-reads the connected monitors from Windows and re-matches them to the saved profiles.</summary>
    public void RefreshMonitors()
    {
        List<DetectedMonitor> detected;
        try {
            detected = DisplayEnumerator.Enumerate();
        }
        catch (Exception e) {
            // keep working with the last known layout; the next display change will try again
            Log.Write("Enumerating displays failed", e);
            return;
        }

        var profilesBefore = Config.Monitors.Count;
        var fingerprintsBefore = Config.Monitors.Select(m => m.Fingerprint).ToList();

        Online = MonitorMatcher.Assign(Config, detected);

        if (Config.Monitors.Count != profilesBefore || !Config.Monitors.Take(profilesBefore).Select(m => m.Fingerprint).SequenceEqual(fingerprintsBefore)) {
            ScheduleSave();
        }

        _engine.Topology = Topology.Build(Config, Online);
        Changed?.Invoke();
    }

    /// <summary>Call after editing <see cref="Config"/>: applies the change to the running hook at once and saves it shortly after.</summary>
    public void Commit()
    {
        _engine.Topology = Topology.Build(Config, Online);
        ScheduleSave();
        Changed?.Invoke();
    }

    public void SetEnabled(bool enabled)
    {
        Config.Settings.Enabled = enabled;
        _hook.Enabled = enabled;
        ScheduleSave();
        Changed?.Invoke();
    }

    public void DeleteLink(Link link)
    {
        Config.Links.Remove(link);
        Commit();
    }

    /// <summary>Forgets a monitor that is not connected, together with its links.</summary>
    public void RemoveProfile(MonitorProfile profile)
    {
        if (FindOnline(profile.Id) != null) return;

        Config.Links.RemoveAll(l => l.A.MonitorId == profile.Id || l.B.MonitorId == profile.Id);
        Config.Monitors.Remove(profile);
        Commit();
    }

    /// <summary>Corrects a mix-up between two displays Windows cannot tell apart.</summary>
    public void SwapIdentity(MonitorProfile a, MonitorProfile b)
    {
        MonitorMatcher.SwapIdentity(a, b);
        ScheduleSave();
        RefreshMonitors();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (!_saveTimer.IsEnabled) return;

        _saveTimer.Stop();
        try {
            _store.Save(Config);
        }
        catch (Exception e) {
            Log.Write("Saving the configuration failed", e);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _dispatcher.BeginInvoke(() => {
            _refreshTimer.Stop();
            _refreshTimer.Start();
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _dispatcher.BeginInvoke(AfterWake);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect) _dispatcher.BeginInvoke(AfterWake);
    }

    private void AfterWake()
    {
        _hook.Reinstall();
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        _hook.Dispose();
        SaveNow();
    }
}

internal static class Log {
    private static readonly object Gate = new();

    public static void Write(string message, Exception? exception = null)
    {
        try {
            lock (Gate) {
                Directory.CreateDirectory(AppController.DataDirectory);
                File.AppendAllText(Path.Combine(AppController.DataDirectory, "edgelink.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(exception == null ? "" : Environment.NewLine + exception)}{Environment.NewLine}");
            }
        }
        catch (Exception) {
            // logging must never take the app down
        }
    }
}
