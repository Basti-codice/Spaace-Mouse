using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using SpaceMouse.Core.Persistence;

namespace SpaceMouse.App.Services;

public static class ThemeService
{
    private static ThemeSetting _setting = ThemeSetting.System;
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Initialize()
    {
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (_setting == ThemeSetting.System && SystemIsDark() != IsDark) Application.Current?.Dispatcher.BeginInvoke(new Action(() => Apply(_setting)));
        };
    }

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(ThemeSetting setting)
    {
        _setting = setting;
        IsDark = setting == ThemeSetting.Dark || (setting == ThemeSetting.System && SystemIsDark());
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/SpaceMouseStudio;component/Themes/{(IsDark ? "Dark" : "Light")}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        // slot 0 is always the colour dictionary
        var first = merged.Count > 0 ? merged[0].Source?.OriginalString ?? "" : "";
        if (first.Contains("Themes/Light") || first.Contains("Themes/Dark"))
            merged[0] = dict;
        else merged.Insert(0, dict);
        foreach (Window w in Application.Current.Windows) ApplyToWindow(w);
        Changed?.Invoke();
    }

    public static void ApplyToWindow(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            var dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(h, 20, ref dark, sizeof(int));   // immersive dark mode (Win10 2004+ / Win11)
        }
        catch { /* cosmetic only */ }
    }

    public static Color ColorOf(string key) => Application.Current.Resources[key] is Color c ? c : Colors.Gray;
}
