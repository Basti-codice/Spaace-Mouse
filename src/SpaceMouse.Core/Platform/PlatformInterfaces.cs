namespace SpaceMouse.Core.Platform;

public enum MouseButton { Left, Right, Middle }

public enum SnapPosition { Left, Right, Top, Bottom, Center }

public sealed record WindowInfo(nint Handle, string Title, string ProcessName);

/// <summary>Keyboard/mouse injection. The Windows implementation uses SendInput.</summary>
public interface IInputSimulator
{
    void MoveCursorBy(int dx, int dy);
    void SetCursor(int x, int y);
    (int X, int Y) GetCursor();
    void MouseDown(MouseButton b);
    void MouseUp(MouseButton b);
    /// <summary>Wheel notches in 1/120 units (positive = up/away).</summary>
    void Wheel(int delta);
    void HWheel(int delta);
    void KeyDown(ushort vk);
    void KeyUp(ushort vk);
    /// <summary>Release every key/button this simulator pressed. Must always be safe to call.</summary>
    void ReleaseAll();
}

public interface IWindowService
{
    WindowInfo? GetForeground();
    WindowInfo? WindowAt(int x, int y);
    void MoveBy(nint handle, int dx, int dy);
    void Maximize(nint handle);
    void Minimize(nint handle);
    void Restore(nint handle);
    void Close(nint handle);
    void Snap(nint handle, SnapPosition pos);
    void MoveToNextMonitor(nint handle);
}

public interface ISystemService
{
    /// <summary>Starts a program, document or URL via the shell. Throws on failure.</summary>
    void Launch(string target, string? args = null);
    /// <summary>Focuses a running process' main window or launches it. Returns false if neither worked.</summary>
    bool FocusOrLaunch(string processOrPath, string? args = null);
    string? ForegroundProcessName();
}

public interface IPlatform
{
    IInputSimulator Input { get; }
    IWindowService Windows { get; }
    ISystemService System { get; }
}
