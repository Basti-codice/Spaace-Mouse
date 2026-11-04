namespace SpaceMouse.Core.Platform;

/// <summary>
/// Platform that performs nothing and records what would have happened. Used for the virtual-controller sandbox,
/// for non-Windows hosts and for tests. Thread-safe.
/// </summary>
public sealed class DryRunPlatform : IPlatform, IInputSimulator, IWindowService, ISystemService
{
    private readonly object _l = new();
    private int _x = 960, _y = 540;
    public List<string> Log { get; } = new();
    public event Action<string>? Logged;
    public WindowInfo? Foreground { get; set; } = new(1, "Dry-run window", "dryrun");
    public string? ForegroundProcess { get; set; } = "dryrun";

    public IInputSimulator Input => this;
    public IWindowService Windows => this;
    public ISystemService System => this;

    private void Rec(string s) { lock (_l) { Log.Add(s); if (Log.Count > 2000) Log.RemoveRange(0, 1000); } Logged?.Invoke(s); }

    public string[] Snapshot() { lock (_l) return Log.ToArray(); }
    public void Clear() { lock (_l) Log.Clear(); }

    public void MoveCursorBy(int dx, int dy) { lock (_l) { _x += dx; _y += dy; } Rec($"cursor {dx:+0;-0},{dy:+0;-0}"); }
    public void SetCursor(int x, int y) { lock (_l) { _x = x; _y = y; } Rec($"cursor@{x},{y}"); }
    public (int X, int Y) GetCursor() { lock (_l) return (_x, _y); }
    public void MouseDown(MouseButton b) => Rec($"mouse-down {b}");
    public void MouseUp(MouseButton b) => Rec($"mouse-up {b}");
    public void Wheel(int delta) => Rec($"wheel {delta}");
    public void HWheel(int delta) => Rec($"hwheel {delta}");
    public void KeyDown(ushort vk) => Rec($"key-down 0x{vk:X2}");
    public void KeyUp(ushort vk) => Rec($"key-up 0x{vk:X2}");
    public void ReleaseAll() => Rec("release-all");

    public WindowInfo? GetForeground() => Foreground;
    public WindowInfo? WindowAt(int x, int y) => Foreground;
    public void MoveBy(nint h, int dx, int dy) => Rec($"window-move {dx:+0;-0},{dy:+0;-0}");
    public void Maximize(nint h) => Rec("window-maximize");
    public void Minimize(nint h) => Rec("window-minimize");
    public void Restore(nint h) => Rec("window-restore");
    public void Close(nint h) => Rec("window-close");
    public void Snap(nint h, SnapPosition p) => Rec($"window-snap {p}");
    public void MoveToNextMonitor(nint h) => Rec("window-next-monitor");

    public void Launch(string target, string? args = null) => Rec($"launch {target} {args}".TrimEnd());
    public bool FocusOrLaunch(string p, string? args = null) { Rec($"focus-or-launch {p}"); return true; }
    string? ISystemService.ForegroundProcessName() => ForegroundProcess;
}

/// <summary>Lets the app swap between the real Windows platform and the dry-run platform at runtime.</summary>
public sealed class SwitchablePlatform : IPlatform
{
    private readonly IPlatform _live;
    private readonly DryRunPlatform _dry;
    public SwitchablePlatform(IPlatform live, DryRunPlatform dry) { _live = live; _dry = dry; }
    public bool Live { get; set; } = true;
    public IPlatform Current => Live ? _live : _dry;
    public DryRunPlatform DryRun => _dry;
    public IInputSimulator Input => Current.Input;
    public IWindowService Windows => Current.Windows;
    public ISystemService System => Current.System;
}
