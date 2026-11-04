using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SpaceMouse.App.Services;

/// <summary>Helpers for borderless always-on-top windows that never take focus (quick menu, toasts).</summary>
public static class OverlayHelper
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_LAYERED = 0x80000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    /// <summary>Call from SourceInitialized: no focus, no taskbar button, optionally click-through.</summary>
    public static void MakeOverlay(Window w, bool clickThrough = true)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        var ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
        if (clickThrough) ex |= WS_EX_TRANSPARENT;
        SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>DPI scale (1.0 = 96 dpi) of the monitor that currently hosts the window.</summary>
    public static double Scale(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        return h == IntPtr.Zero ? 1.0 : Math.Max(96, GetDpiForWindow(h)) / 96.0;
    }

    /// <summary>
    /// Centres the window (size given in DIPs) on a physical screen point, clamped to that monitor's work area,
    /// and shows it topmost without activating. Positioning twice makes WPF pick up the target monitor's DPI first.
    /// </summary>
    public static void ShowCentered(Window w, int px, int py, double widthDip, double heightDip)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        SetWindowPos(h, HWND_TOPMOST, px, py, 1, 1, SWP_NOACTIVATE | SWP_SHOWWINDOW);   // hop onto the right monitor
        var s = Scale(w);
        int wp = (int)Math.Round(widthDip * s), hp = (int)Math.Round(heightDip * s);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(new POINT { X = px, Y = py }, 2), ref mi);
        int x = Math.Clamp(px - wp / 2, mi.rcWork.Left, Math.Max(mi.rcWork.Left, mi.rcWork.Right - wp));
        int y = Math.Clamp(py - hp / 2, mi.rcWork.Top, Math.Max(mi.rcWork.Top, mi.rcWork.Bottom - hp));
        w.Width = widthDip; w.Height = heightDip;
        SetWindowPos(h, HWND_TOPMOST, x, y, wp, hp, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Offset (DIPs) of a physical point relative to the window's top-left, after <see cref="ShowCentered"/> clamping.</summary>
    public static Point PointInWindow(Window w, int px, int py)
    {
        var h = new WindowInteropHelper(w).Handle;
        GetWindowRectEx(h, out var r);
        var s = Scale(w);
        return new Point((px - r.Left) / s, (py - r.Top) / s);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")] private static extern bool GetWindowRectEx(IntPtr h, out RECT r);

    /// <summary>Work area (physical px) of the primary monitor.</summary>
    public static (int Left, int Top, int Right, int Bottom) PrimaryWorkArea()
    {
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1), ref mi);
        return (mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
    }

    public static void PlaceBottomCenter(Window w, double widthDip, double heightDip, double marginDip)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        var a = PrimaryWorkArea();
        var s = Scale(w);
        int wp = (int)Math.Round(widthDip * s), hp = (int)Math.Round(heightDip * s), m = (int)Math.Round(marginDip * s);
        w.Width = widthDip; w.Height = heightDip;
        SetWindowPos(h, HWND_TOPMOST, a.Left + (a.Right - a.Left - wp) / 2, a.Bottom - hp - m, wp, hp, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
