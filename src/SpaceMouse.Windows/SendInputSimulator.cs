using System.Runtime.InteropServices;
using SpaceMouse.Core.Platform;

namespace SpaceMouse.Windows;

/// <summary>
/// Keyboard and mouse injection via SendInput. Remembers every key/button it pressed so <see cref="ReleaseAll"/>
/// can guarantee nothing stays stuck. Injected events carry a marker in dwExtraInfo.
/// </summary>
public sealed class SendInputSimulator : IInputSimulator
{
    public const uint Marker = 0x53_4D_53_54; // "SMST"
    private readonly object _l = new();
    private readonly HashSet<ushort> _keys = new();
    private readonly HashSet<MouseButton> _buttons = new();

    /// <summary>Raised when Windows refuses injection (e.g. target window is elevated and we are not).</summary>
    public event Action<string>? Failed;

    private static readonly int Size = Marshal.SizeOf<Native.INPUT>();

    private void Send(params Native.INPUT[] inputs)
    {
        var sent = Native.SendInput((uint)inputs.Length, inputs, Size);
        if (sent != inputs.Length)
            Failed?.Invoke("Windows blocked the input (error " + Marshal.GetLastWin32Error() + "). If the target app runs as administrator, start Space Mouse Studio as administrator too.");
    }

    private static Native.INPUT Mouse(uint flags, int dx = 0, int dy = 0, uint data = 0) => new()
    {
        type = Native.INPUT_MOUSE,
        u = new Native.INPUTUNION { mi = new Native.MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = Marker } },
    };

    private static bool Extended(ushort vk) => vk is (>= 0x21 and <= 0x28) or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or (>= 0xA6 and <= 0xB7) || vk == 0xA3 || vk == 0xA5;

    private static Native.INPUT Key(ushort vk, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        u = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)Native.MapVirtualKey(vk, 0),
                dwFlags = (Extended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0) | (up ? Native.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Marker,
            },
        },
    };

    public (int X, int Y) GetCursor() { Native.GetCursorPos(out var p); return (p.X, p.Y); }

    public void SetCursor(int x, int y) => MoveAbsolute(x, y);

    public void MoveCursorBy(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        Native.GetCursorPos(out var p);
        MoveAbsolute(p.X + dx, p.Y + dy);
    }

    // Absolute positioning avoids Windows' "enhance pointer precision" acceleration that relative moves would get,
    // so cursor speed is exactly what the profile says. Coordinates are clamped to the virtual desktop (all monitors).
    private void MoveAbsolute(int x, int y)
    {
        int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN)), vh = Math.Max(2, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
        x = Math.Clamp(x, vx, vx + vw - 1); y = Math.Clamp(y, vy, vy + vh - 1);
        var ax = (int)Math.Ceiling((x - vx) * 65535.0 / (vw - 1));
        var ay = (int)Math.Ceiling((y - vy) * 65535.0 / (vh - 1));
        Send(Mouse(Native.MOUSEEVENTF_MOVE | Native.ABSOLUTE | Native.VIRTUALDESK, ax, ay));
    }

    public void MouseDown(MouseButton b)
    {
        lock (_l) _buttons.Add(b);
        Send(Mouse(b switch { MouseButton.Left => Native.LEFTDOWN, MouseButton.Right => Native.RIGHTDOWN, _ => Native.MIDDLEDOWN }));
    }

    public void MouseUp(MouseButton b)
    {
        lock (_l) _buttons.Remove(b);
        Send(Mouse(b switch { MouseButton.Left => Native.LEFTUP, MouseButton.Right => Native.RIGHTUP, _ => Native.MIDDLEUP }));
    }

    public void Wheel(int delta) { if (delta != 0) Send(Mouse(Native.WHEEL, data: unchecked((uint)delta))); }
    public void HWheel(int delta) { if (delta != 0) Send(Mouse(Native.HWHEEL, data: unchecked((uint)delta))); }

    public void KeyDown(ushort vk) { lock (_l) _keys.Add(vk); Send(Key(vk, false)); }
    public void KeyUp(ushort vk) { lock (_l) _keys.Remove(vk); Send(Key(vk, true)); }

    public void ReleaseAll()
    {
        ushort[] keys; MouseButton[] buttons;
        lock (_l) { keys = _keys.ToArray(); buttons = _buttons.ToArray(); _keys.Clear(); _buttons.Clear(); }
        foreach (var k in keys) { try { Send(Key(k, true)); } catch { } }
        foreach (var b in buttons)
        {
            try { Send(Mouse(b switch { MouseButton.Left => Native.LEFTUP, MouseButton.Right => Native.RIGHTUP, _ => Native.MIDDLEUP })); } catch { }
        }
    }
}
