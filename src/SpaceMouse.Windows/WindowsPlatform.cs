using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SpaceMouse.Core.Platform;

namespace SpaceMouse.Windows;

public sealed class WindowService : IWindowService
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "NotifyIconOverflowWindow", "TaskListThumbnailWnd" };
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    private static string Text(nint h)
    {
        var b = new char[256];
        var n = Native.GetWindowText(h, b, b.Length);
        return new string(b, 0, Math.Max(0, n));
    }
    private static string ClassName(nint h)
    {
        var b = new char[128];
        var n = Native.GetClassName(h, b, b.Length);
        return new string(b, 0, Math.Max(0, n));
    }

    private static string ProcessName(uint pid)
    {
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; } catch { return ""; }
    }

    private WindowInfo? Info(nint h, bool skipOwn)
    {
        if (h == 0 || !Native.IsWindow(h)) return null;
        Native.GetWindowThreadProcessId(h, out var pid);
        if (skipOwn && pid == _ownPid) return null;
        if (ShellClasses.Contains(ClassName(h))) return null;
        return new WindowInfo(h, Text(h), ProcessName(pid));
    }

    public WindowInfo? GetForeground() => Info(Native.GetForegroundWindow(), skipOwn: false) is { } w ? w : null;

    public WindowInfo? WindowAt(int x, int y)
    {
        var h = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
        if (h == 0) return null;
        return Info(Native.GetAncestor(h, Native.GA_ROOT), skipOwn: true);
    }

    private static void Require(nint h) { if (h == 0 || !Native.IsWindow(h)) throw new InvalidOperationException("The window is gone."); }

    public void MoveBy(nint h, int dx, int dy)
    {
        Require(h);
        if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE); // dragging a maximised window restores it, like the title bar does
        Native.GetWindowRect(h, out var r);
        Native.SetWindowPos(h, 0, r.Left + dx, r.Top + dy, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public void Maximize(nint h) { Require(h); Native.ShowWindow(h, Native.SW_MAXIMIZE); }
    public void Minimize(nint h) { Require(h); Native.ShowWindow(h, Native.SW_MINIMIZE); }
    public void Restore(nint h) { Require(h); Native.ShowWindow(h, Native.SW_RESTORE); }
    public void Close(nint h) { Require(h); Native.PostMessage(h, Native.WM_CLOSE, 0, 0); } // polite close: apps may ask to save

    private static Native.RECT WorkArea(nint h)
    {
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST), ref mi);
        return mi.rcWork;
    }

    /// <summary>Invisible resize borders of Win10/11 windows (difference between window rect and visible frame).</summary>
    private static (int L, int T, int R, int B) Shadow(nint h)
    {
        Native.GetWindowRect(h, out var w);
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var f, Marshal.SizeOf<Native.RECT>()) != 0) return (0, 0, 0, 0);
        return (f.Left - w.Left, f.Top - w.Top, w.Right - f.Right, w.Bottom - f.Bottom);
    }

    private static void Place(nint h, int x, int y, int w, int hgt)
    {
        var s = Shadow(h);
        Native.SetWindowPos(h, 0, x - s.L, y - s.T, w + s.L + s.R, hgt + s.T + s.B, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    public void Snap(nint h, SnapPosition pos)
    {
        Require(h);
        if (Native.IsZoomed(h) || Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        var a = WorkArea(h);
        int w = a.Width, hh = a.Height;
        switch (pos)
        {
            case SnapPosition.Left: Place(h, a.Left, a.Top, w / 2, hh); break;
            case SnapPosition.Right: Place(h, a.Left + w / 2, a.Top, w - w / 2, hh); break;
            case SnapPosition.Top: Place(h, a.Left, a.Top, w, hh / 2); break;
            case SnapPosition.Bottom: Place(h, a.Left, a.Top + hh / 2, w, hh - hh / 2); break;
            case SnapPosition.Center:
                Native.GetWindowRect(h, out var r);
                Place(h, a.Left + (w - r.Width) / 2, a.Top + (hh - r.Height) / 2, r.Width, r.Height); break;
        }
    }

    public void MoveToNextMonitor(nint h)
    {
        Require(h);
        var mons = new List<Native.RECT>();
        Native.EnumDisplayMonitors(0, 0, (nint m, nint dc, ref Native.RECT r, nint d) => { mons.Add(r); return true; }, 0);
        if (mons.Count < 2) return;
        mons = mons.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
        var curMon = Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(curMon, ref mi);
        var idx = mons.FindIndex(m => m.Left == mi.rcMonitor.Left && m.Top == mi.rcMonitor.Top);
        var next = mons[(idx + 1) % mons.Count];
        var wasMax = Native.IsZoomed(h);
        if (wasMax) Native.ShowWindow(h, Native.SW_RESTORE);
        Native.GetWindowRect(h, out var r);
        // keep relative position and scale to the new monitor's size
        double rx = (r.Left - mi.rcMonitor.Left) / (double)Math.Max(1, mi.rcMonitor.Width);
        double ry = (r.Top - mi.rcMonitor.Top) / (double)Math.Max(1, mi.rcMonitor.Height);
        double sw = r.Width / (double)Math.Max(1, mi.rcMonitor.Width), sh = r.Height / (double)Math.Max(1, mi.rcMonitor.Height);
        Native.SetWindowPos(h, 0, next.Left + (int)(rx * next.Width), next.Top + (int)(ry * next.Height),
            (int)(sw * next.Width), (int)(sh * next.Height), Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        if (wasMax) Native.ShowWindow(h, Native.SW_MAXIMIZE);
    }
}

public sealed class ShellSystemService : ISystemService
{
    public void Launch(string target, string? args = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, Arguments = args ?? "", UseShellExecute = true });
        }
        catch (Win32Exception) when (!target.Contains('.') && !target.Contains('\\') && !target.Contains('/'))
        {
            Process.Start(new ProcessStartInfo { FileName = target + ".exe", Arguments = args ?? "", UseShellExecute = true });
        }
    }

    public bool FocusOrLaunch(string processOrPath, string? args = null)
    {
        var name = Path.GetFileNameWithoutExtension(processOrPath.Trim('"'));
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    var h = p.MainWindowHandle;
                    if (h == 0) continue;
                    BringToFront(h);
                    return true;
                }
            }
        }
        catch { /* fall through to launch */ }
        try { Launch(processOrPath, args); return true; } catch { return false; }
    }

    private static void BringToFront(nint h)
    {
        if (Native.IsIconic(h)) Native.ShowWindow(h, Native.SW_RESTORE);
        var fg = Native.GetForegroundWindow();
        var fgThread = Native.GetWindowThreadProcessId(fg, out _);
        var me = Native.GetCurrentThreadId();
        // AttachThreadInput lets us pass Windows' foreground lock.
        var attached = fgThread != me && Native.AttachThreadInput(me, fgThread, true);
        try { Native.BringWindowToTop(h); Native.SetForegroundWindow(h); }
        finally { if (attached) Native.AttachThreadInput(me, fgThread, false); }
    }

    public string? ForegroundProcessName()
    {
        var h = Native.GetForegroundWindow();
        if (h == 0) return null;
        Native.GetWindowThreadProcessId(h, out var pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; } catch { return null; }
    }
}

public sealed class WindowsPlatform : IPlatform
{
    public SendInputSimulator Simulator { get; } = new();
    public IInputSimulator Input => Simulator;
    public IWindowService Windows { get; } = new WindowService();
    public ISystemService System { get; } = new ShellSystemService();
}
