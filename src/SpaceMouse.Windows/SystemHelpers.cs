using Microsoft.Win32;
using SpaceMouse.Core.Actions;

namespace SpaceMouse.Windows;

/// <summary>Global toggle hotkey (RegisterHotKey) - a guaranteed way to switch the SpaceMouse control off.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int Id = 0x5350;
    private MessageLoopWindow? _win;
    public event Action? Pressed;
    public string? LastError { get; private set; }

    public bool Register(string combo)
    {
        Unregister();
        LastError = null;
        if (string.IsNullOrWhiteSpace(combo)) return true;
        var chord = KeyCombo.ParseChord(combo);
        if (chord == null || chord.Length < 2) { LastError = "Invalid hotkey. Use e.g. Ctrl+Alt+S."; return false; }
        uint mods = 0; ushort key = 0;
        foreach (var k in chord)
        {
            if (k == KeyCombo.VK_CONTROL) mods |= Native.MOD_CONTROL;
            else if (k == KeyCombo.VK_SHIFT) mods |= Native.MOD_SHIFT;
            else if (k == KeyCombo.VK_MENU) mods |= Native.MOD_ALT;
            else if (k == KeyCombo.VK_LWIN) mods |= Native.MOD_WIN;
            else key = k;
        }
        if (key == 0 || mods == 0) { LastError = "A hotkey needs at least one modifier and one key."; return false; }
        try
        {
            _win = new MessageLoopWindow((m, w, l) => { if (m == Native.WM_HOTKEY && (int)w == Id) { Pressed?.Invoke(); return true; } return false; });
            var ok = false;
            _win.Invoke(() => ok = Native.RegisterHotKey(_win.Handle, Id, mods | Native.MOD_NOREPEAT, key));
            if (!ok) { LastError = "This hotkey is already used by another program."; Unregister(); }
            return ok;
        }
        catch (Exception e) { LastError = e.Message; return false; }
    }

    public void Unregister()
    {
        if (_win == null) return;
        try { var w = _win; w.Invoke(() => Native.UnregisterHotKey(w.Handle, Id)); } catch { }
        _win.Dispose(); _win = null;
    }

    public void Dispose() => Unregister();
}

public static class AutoStart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SpaceMouseStudio";

    public static bool IsEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(ValueName) != null; } catch { return false; }
    }

    public static void Set(bool enabled, string exePath)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, true) ?? Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) k.SetValue(ValueName, $"\"{exePath}\" --minimized");
        else k.DeleteValue(ValueName, false);
    }
}

/// <summary>Raises Windows timer resolution to 1 ms for a steadier ~125 Hz engine loop.</summary>
public sealed class TimerResolution : IDisposable
{
    public TimerResolution() { try { Native.timeBeginPeriod(1); } catch { } }
    public void Dispose() { try { Native.timeEndPeriod(1); } catch { } }
}
