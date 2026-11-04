using System.Diagnostics;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Persistence;

namespace SpaceMouse.App.Pages;

public partial class SettingsPage : UserControl, IPage
{
    private readonly AppController _app = AppController.Instance;
    private TextBlock? _hotkeyHint;
    private bool _recordHotkey;

    public SettingsPage()
    {
        InitializeComponent();
        Build();
    }

    public void OnShown() { Build(); }

    private void Changed() => _app.ApplySettings();

    private void Build()
    {
        Host.Children.Clear();
        var s = _app.Settings;

        // ---------------- appearance
        var appearance = Card("Appearance");
        appearance.Children.Add(Row("Theme", "Light, dark, or follow Windows.", Segmented(
            new[] { "System", "Light", "Dark" }, (int)s.Theme, i => { s.Theme = (ThemeSetting)i; Changed(); })));
        Host.Children.Add(Wrap(appearance));

        // ---------------- startup
        var startup = Card("Startup & tray");
        startup.Children.Add(Row("Start with Windows", "Launches in the background when you sign in.", Switch(s.StartWithWindows, v => { s.StartWithWindows = v; Changed(); })));
        startup.Children.Add(Row("Start minimised", "Do not open the window at launch.", Switch(s.StartMinimized, v => { s.StartMinimized = v; Changed(); })));
        startup.Children.Add(Row("Keep running in the tray when closing the window", "Quit from the tray icon.", Switch(s.MinimizeToTray, v => { s.MinimizeToTray = v; Changed(); })));
        startup.Children.Add(Row("Turn control on at start", "Off by default so your mouse is never surprised.", Switch(s.ActivateOnStart, v => { s.ActivateOnStart = v; Changed(); })));
        Host.Children.Add(Wrap(startup));

        // ---------------- safety
        var safety = Card("Safety");
        var hk = new TextBox { Text = s.ToggleHotkey, Width = 180 };
        var rec = new Button { Content = "Record", Margin = new Thickness(8, 0, 0, 0) };
        _hotkeyHint = new TextBlock { Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        hk.LostKeyboardFocus += (_, _) => _recordHotkey = false;
        hk.PreviewKeyDown += (_, e) =>
        {
            if (!_recordHotkey) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var name = Controls.ActionPicker.KeyName(key);
            if (name == null) return;
            var parts = new List<string>();
            var m = Keyboard.Modifiers;
            if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            if (parts.Count == 0) { _hotkeyHint!.Text = "Use at least one modifier (Ctrl, Alt, Shift or Win)."; return; }
            parts.Add(name);
            hk.Text = string.Join("+", parts);
            _recordHotkey = false; rec.Content = "Record";
        };
        rec.Click += (_, _) => { _recordHotkey = true; rec.Content = "Press keys..."; hk.Focus(); };
        void ApplyHotkey()
        {
            s.ToggleHotkey = hk.Text.Trim();
            _app.ApplySettings();
            _hotkeyHint!.Text = _app.HotkeyError != null ? _app.HotkeyError
                : string.IsNullOrEmpty(s.ToggleHotkey) ? "Hotkey disabled." : $"Press {s.ToggleHotkey} anywhere to switch SpaceMouse control on/off.";
            _hotkeyHint.SetResourceReference(TextBlock.ForegroundProperty, _app.HotkeyError != null ? "DangerBrush" : "MutedBrush");
        }
        var apply = new Button { Content = "Apply", Margin = new Thickness(8, 0, 0, 0) };
        apply.Click += (_, _) => ApplyHotkey();
        hk.KeyDown += (_, e) => { if (e.Key == Key.Enter && !_recordHotkey) ApplyHotkey(); };
        var hkRow = new StackPanel { Orientation = Orientation.Horizontal };
        hkRow.Children.Add(hk); hkRow.Children.Add(rec); hkRow.Children.Add(apply);
        var hkPanel = new StackPanel();
        hkPanel.Children.Add(hkRow); hkPanel.Children.Add(_hotkeyHint);
        safety.Children.Add(Row("Panic hotkey", "Works from any program. Always gets you back to normal.", hkPanel, vertical: true));
        _hotkeyHint.Text = _app.HotkeyError ?? (string.IsNullOrEmpty(s.ToggleHotkey) ? "Hotkey disabled." : $"Registered: {s.ToggleHotkey}");
        safety.Children.Add(Row("Show small notifications", "Profile changes and errors appear briefly at the bottom of the screen.", Switch(s.ShowToasts, v => { s.ShowToasts = v; Changed(); })));
        safety.Children.Add(Text("Whenever control is switched off, a profile changes, the device is unplugged or the app quits, all held keys and mouse buttons are released automatically.", muted: true, top: 6));
        Host.Children.Add(Wrap(safety));

        // ---------------- data
        var data = Card("Your data");
        data.Children.Add(Text("Profiles, hardware calibration and settings are stored as readable JSON files:", muted: true));
        data.Children.Add(new TextBox { Text = _app.Store.Directory_, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 8), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12 });
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        var open = new Button { Content = "Open folder" };
        open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = _app.Store.Directory_, UseShellExecute = true }); } catch (Exception e) { _app.Toast(e.Message, true); } };
        var reset = new Button { Content = "Restore all presets", Margin = new Thickness(8, 0, 0, 0) };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show("Reset all built-in presets to their original settings? Your own profiles stay untouched.", "Restore presets", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (var p in _app.Profiles.Where(p => p.PresetKey != null).ToList()) _app.ResetPreset(p);
            _app.Toast("Presets restored", false);
        };
        btns.Children.Add(open); btns.Children.Add(reset);
        data.Children.Add(btns);
        Host.Children.Add(Wrap(data));

        // ---------------- about
        var about = Card("About");
        var v = typeof(App).Assembly.GetName().Version;
        about.Children.Add(Text($"Space Mouse Studio {v?.Major}.{v?.Minor}.{v?.Build}", bold: true));
        about.Children.Add(Text("Independent project - not affiliated with or endorsed by 3Dconnexion. It never replaces or disables the official driver; it reads the device in parallel (Windows Raw Input).", muted: true, top: 4));
        about.Children.Add(Text($"Actions in library: {_app.Registry.All.Count()}   ·   Profiles: {_app.Profiles.Count}", muted: true, top: 4));
        Host.Children.Add(Wrap(about));
    }

    // ------------------------------------------------------------------ helpers
    private StackPanel Card(string title)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("H2"), Margin = new Thickness(0, 0, 0, 8) });
        return sp;
    }

    private Border Wrap(StackPanel content) => new()
    {
        Style = (Style)FindResource("Card"), Child = content, Margin = new Thickness(0, 0, 0, 16),
    };

    private TextBlock Text(string t, bool muted = false, bool bold = false, double top = 0)
    {
        var tb = new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0) };
        if (muted) tb.Style = (Style)FindResource("Muted");
        if (bold) tb.FontWeight = FontWeights.SemiBold;
        return tb;
    }

    private UIElement Row(string title, string desc, UIElement control, bool vertical = false)
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        if (desc.Length > 0) left.Children.Add(new TextBlock { Text = desc, Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        if (vertical)
        {
            left.Children.Add(new ContentControl { Content = control, Margin = new Thickness(0, 8, 0, 0) });
            return left;
        }
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.Margin = new Thickness(0, 0, 20, 0);
        Grid.SetColumn(control as FrameworkElement ?? new Border(), 1);
        g.Children.Add(left);
        if (control is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(fe); }
        return g;
    }

    private CheckBox Switch(bool value, Action<bool> on)
    {
        var cb = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = value };
        cb.Click += (_, _) => on(cb.IsChecked == true);
        return cb;
    }

    private FrameworkElement Segmented(string[] names, int selected, Action<int> on)
    {
        var host = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(3) };
        host.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        var current = selected;
        for (var i = 0; i < names.Length; i++)
        {
            var idx = i;
            var rb = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = group, Content = names[i], IsChecked = i == selected, MinWidth = 76 };
            rb.Checked += (_, _) => { if (idx != current) { current = idx; on(idx); } };
            panel.Children.Add(rb);
        }
        host.Child = panel;
        return host;
    }
}
