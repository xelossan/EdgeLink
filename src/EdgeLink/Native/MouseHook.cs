using EdgeLink.Core;
using static EdgeLink.Native.NativeMethods;

namespace EdgeLink.Native;

/// <summary>
/// Runs a low-level mouse hook on its own thread, so that a busy UI can never delay the cursor,
/// and feeds every physical mouse move through the <see cref="CrossingEngine"/>.
/// </summary>
internal sealed unsafe class MouseHook : IDisposable {
    private const uint ReinstallMessage = WM_APP + 1;
    private const int HotkeyId = 1;

    // mouse events synthesised from touch or pen input carry this signature in dwExtraInfo
    private const ulong TouchSignatureMask = 0xFFFFFF00;
    private const ulong TouchSignature = 0xFF515700;

    private readonly CrossingEngine _engine;
    private readonly LowLevelMouseProc _callback; // kept in a field so the delegate is not collected
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private bool _moving;

    public MouseHook(CrossingEngine engine)
    {
        _engine = engine;
        _callback = Callback;
    }

    public volatile bool Enabled;

    /// <summary>False if another program already owns the pause hotkey.</summary>
    public bool HotkeyRegistered { get; private set; }

    /// <summary>Raised on the hook thread when Ctrl+Alt+Shift+E is pressed.</summary>
    public event Action? HotkeyPressed;

    public void Start()
    {
        if (_thread != null) return;

        _thread = new Thread(Run) { IsBackground = true, Name = "EdgeLink mouse hook", Priority = ThreadPriority.Highest };
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>
    /// Windows silently drops low-level hooks in some situations (for example after sleep), and
    /// there is no notification for it; re-installing is cheap and harmless.
    /// </summary>
    public void Reinstall()
    {
        if (_threadId != 0) PostThreadMessage(_threadId, ReinstallMessage, UIntPtr.Zero, IntPtr.Zero);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        Install();
        HotkeyRegistered = RegisterHotKey(IntPtr.Zero, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, 'E');
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0) {
            if (msg.message == WM_HOTKEY) {
                HotkeyPressed?.Invoke();
            }
            else if (msg.message == ReinstallMessage) {
                Uninstall();
                Install();
            }
        }

        UnregisterHotKey(IntPtr.Zero, HotkeyId);
        Uninstall();
    }

    private void Install()
    {
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _callback, GetModuleHandle(null), 0);
    }

    private void Uninstall()
    {
        if (_hook != IntPtr.Zero) {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        // _moving: SetCursorPos below can re-enter this callback with our own jump
        if (code >= 0 && wParam == WM_MOUSEMOVE && Enabled && !_moving) {
            try {
                var info = (MSLLHOOKSTRUCT*) lParam;

                // only hardware mouse motion is steered: programmatic jumps (automation, remote
                // control, touch on a tablet screen) are aimed at an exact spot and must reach it
                var synthetic = (info->flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0
                                || ((ulong) info->dwExtraInfo & TouchSignatureMask) == TouchSignature;

                if (!synthetic && GetCursorPos(out var current) && !IsClippedByAnotherProgram()) {
                    var from = new PixelPoint(current.X, current.Y);
                    if (_engine.TryRedirect(from, new PixelPoint(info->pt.X, info->pt.Y), out var target)) {
                        if (target != from) MoveCursor(target);
                        return 1; // swallow the original move
                    }
                }
            }
            catch (Exception) {
                // never let an exception escape into the system's input pipeline
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>
    /// Games and some full-screen programs confine the cursor to their window; while they do,
    /// the cursor is theirs.
    /// </summary>
    private bool IsClippedByAnotherProgram()
    {
        if (!GetClipCursor(out var clip)) return false;

        var desktop = _engine.Topology.VirtualBounds;
        return clip.Left > desktop.X || clip.Top > desktop.Y || clip.Right < desktop.Right || clip.Bottom < desktop.Bottom;
    }

    private void MoveCursor(PixelPoint target)
    {
        _moving = true;
        try {
            // a jump between monitors with different scaling does not always land where asked on
            // the first try
            for (var attempt = 0; attempt < 3; attempt++) {
                SetCursorPos(target.X, target.Y);
                if (GetCursorPos(out var now) && now.X == target.X && now.Y == target.Y) break;
            }
        }
        finally {
            _moving = false;
        }
    }

    public void Dispose()
    {
        if (_thread == null) return;

        PostThreadMessage(_threadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _thread = null;
    }
}
