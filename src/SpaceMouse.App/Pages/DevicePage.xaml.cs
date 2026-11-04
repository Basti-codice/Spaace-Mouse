using System.Globalization;
using System.Windows.Threading;
using SpaceMouse.App.Controls;
using SpaceMouse.App.Services;
using SpaceMouse.App.Views;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using HwF = SpaceMouse.Core.Hid.HardwareProfiles;

namespace SpaceMouse.App.Pages;

public partial class DevicePage : UserControl, IPage
{
    private readonly AppController _app = AppController.Instance;
    private readonly DispatcherTimer _diag = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _live = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly object _repLock = new();
    private readonly List<string> _reports = new();
    private volatile bool _repDirty;
    private bool _subscribed;

    // connection widgets
    private readonly ContentControl _badgeHost = new();
    private readonly TextBlock _devName = UiKit.T("", "H2"), _devUsb = UiKit.T("", "Muted"), _devManu = UiKit.T("", "Muted");
    private readonly TextBlock _count = UiKit.T("0"), _error = UiKit.T("", null, 12.5, true), _hints = UiKit.T("", "Muted", 12.5, true);
    private readonly RadioButton _rawIn = new() { Content = "Raw Input (recommended)", Style = (Style)Application.Current.Resources["Segment"], GroupName = "inmethod" };
    private readonly RadioButton _hidIn = new() { Content = "Direct HID", Style = (Style)Application.Current.Resources["Segment"], GroupName = "inmethod" };

    // hardware profile widgets
    private readonly StackPanel _profileList = new();
    private readonly TextBlock _layoutWarn = UiKit.T("", null, 12.5, true);
    private readonly StackPanel _axisRows = new();
    private Guid _shownProfile = Guid.Empty;

    // live
    private readonly SpaceMouseView _view = new() { Interactive = false, MaxHeight = 360 };
    private readonly TextBlock _inspector = UiKit.T("", "Mono", 11.5);
    private bool _suppress;

    public DevicePage()
    {
        InitializeComponent();
        Root.Children.Add(BuildConnection());
        Root.Children.Add(BuildProfiles());
        Root.Children.Add(BuildManual());
        Root.Children.Add(BuildLive());

        _diag.Tick += (_, _) => Guard(OnDiagTick);
        _live.Tick += (_, _) => Guard(OnLiveTick);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    public void OnShown() { Start(); RefreshAll(); }

    private static void Guard(Action a) { try { a(); } catch { /* UI refresh must never throw */ } }

    // ------------------------------------------------------------------ lifecycle
    private void Start()
    {
        if (_subscribed) return;
        _subscribed = true;
        _app.StateChanged += OnState;
        _app.Hub.RawReceived += OnRaw;
        _app.HardwareProfiles.CollectionChanged += OnProfilesChanged;
        _diag.Start(); _live.Start();
        RefreshAll();
    }

    private void Stop()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _app.StateChanged -= OnState;
        _app.Hub.RawReceived -= OnRaw;
        _app.HardwareProfiles.CollectionChanged -= OnProfilesChanged;
        _diag.Stop(); _live.Stop();
    }

    private void OnState() => Guard(RefreshAll);
    private void OnProfilesChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Guard(RefreshProfiles);

    private void OnDiagTick()
    {
        _app.RefreshDiagnostics();
        RefreshConnection();
        if (_repDirty)
        {
            _repDirty = false;
            string text;
            lock (_repLock) text = string.Join("\n", _reports);
            _inspector.Text = text.Length == 0 ? "No reports received yet." : text;
        }
    }

    private void OnLiveTick() => _view.UpdateLive(_app.Hub.Current);

    // called from the HID thread
    private DateTime _lastRep = DateTime.MinValue;
    private void OnRaw(RawReport r)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRep).TotalMilliseconds < 40) return; // throttle: the inspector only needs samples
        _lastRep = now;
        lock (_repLock)
        {
            _reports.Add(FormatReport(r.Data));
            while (_reports.Count > 8) _reports.RemoveAt(0);
        }
        _repDirty = true;
    }

    /// <summary>"ID 01 · 7 B · 01 5A 00 ..." - pure formatting helper.</summary>
    public static string FormatReport(byte[] data)
    {
        if (data.Length == 0) return "(empty)";
        var bytes = string.Join(" ", data.Take(24).Select(b => b.ToString("X2")));
        return $"ID {data[0]:X2} · {data.Length} B · {bytes}{(data.Length > 24 ? " ..." : "")}";
    }

    // ------------------------------------------------------------------ (a) connection
    private UIElement BuildConnection()
    {
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Connection", "H2"));
        s.Children.Add(new Border { Height = 12 });
        s.Children.Add(_badgeHost);
        s.Children.Add(new Border { Height = 12 });
        s.Children.Add(_devName); s.Children.Add(_devUsb); s.Children.Add(_devManu);
        s.Children.Add(new Border { Height = 14 });

        s.Children.Add(UiKit.T("How the app reads the device", "H3"));
        var seg = new Border { Style = (Style)Application.Current.Resources["Inset"], Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 4) };
        var segRow = new StackPanel { Orientation = Orientation.Horizontal };
        segRow.Children.Add(_rawIn); segRow.Children.Add(_hidIn);
        seg.Child = segRow;
        _rawIn.Click += (_, _) => SetMethod("RawInput");
        _hidIn.Click += (_, _) => SetMethod("DirectHid");
        s.Children.Add(seg);
        s.Children.Add(UiKit.T("Raw Input shares the device with the official 3Dconnexion driver (nothing is disabled). If it shows no data, try Direct HID.", "Muted", 12, true));

        var cr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        cr.Children.Add(UiKit.T("Reports received: ", "Muted")); cr.Children.Add(_count);
        s.Children.Add(cr);
        _error.Margin = new Thickness(0, 8, 0, 0);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        s.Children.Add(_error);
        _hints.Margin = new Thickness(0, 8, 0, 0);
        s.Children.Add(_hints);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        btns.Children.Add(UiKit.Btn("Calibrate...", () => CalibrationWindow.Run(Window.GetWindow(this)), "PrimaryButton"));
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(UiKit.Btn("Open virtual controller", VirtualControllerWindow.ShowSingleton));
        s.Children.Add(btns);
        return UiKit.Card(s);
    }

    private void SetMethod(string m)
    {
        if (_suppress) return;
        _app.Settings.InputMethod = m;
        _app.SaveSettings();
        _app.ApplyInputMethod();
        RefreshAll();
    }

    private void RefreshConnection()
    {
        var kind = _app.ActiveSource;
        _badgeHost.Content = UiKit.Pill(_app.SourceBadge, kind == SourceKind.Hardware ? "SuccessBrush" : kind == SourceKind.Virtual ? "AccentBrush" : "WarnBrush");
        _devName.Text = _app.SourceTitle;
        var d = _app.Hub.Device;
        _devUsb.Text = kind == SourceKind.Hardware && d != null ? $"USB ID {d.UsbId}" : _app.SourceDetail;
        _devManu.Text = kind == SourceKind.Hardware && d != null && !string.IsNullOrEmpty(d.Manufacturer) ? "Manufacturer: " + d.Manufacturer : "";
        _devManu.Visibility = _devManu.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _count.Text = _app.ReportCount.ToString("N0", CultureInfo.InvariantCulture);
        var err = _app.SourceError;
        _error.Text = err ?? "";
        _error.Visibility = string.IsNullOrEmpty(err) ? Visibility.Collapsed : Visibility.Visible;
        _hints.Text = kind switch
        {
            SourceKind.None when _app.UseVirtual => "The virtual device is unplugged. Plug it in again in the virtual controller window.",
            SourceKind.None => "No SpaceMouse found. Check: 1) the USB cable and port (avoid hubs), 2) the device appears in Windows Device Manager under Human Interface Devices, 3) the official 3Dconnexion driver may stay installed - it is not required but does not hurt, 4) try 'Direct HID' above. No device at hand? Open the virtual controller.",
            SourceKind.Hardware when _app.ReportCount == 0 => "Device found but no data yet. Move the cap once. If the counter stays at 0, try the other input method.",
            _ => "",
        };
        _hints.Visibility = _hints.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _suppress = true;
        _rawIn.IsChecked = _app.Settings.InputMethod != "DirectHid";
        _hidIn.IsChecked = _app.Settings.InputMethod == "DirectHid";
        _suppress = false;
    }

    // ------------------------------------------------------------------ (b) hardware profiles
    private UIElement BuildProfiles()
    {
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Hardware profiles", "H2"));
        s.Children.Add(UiKit.T("A hardware profile describes how your device talks (report layout, axis order, signs, ranges). Keep one per device and switch any time.", "Muted", 12.5, true));
        s.Children.Add(new Border { Height = 10 });
        s.Children.Add(_profileList);
        var add = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        add.Children.Add(UiKit.T("New from template:  ", "Muted")); ((TextBlock)add.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        add.Children.Add(UiKit.Btn("3 reports", () => AddFrom(HwF.Standard3Report())));
        add.Children.Add(new Border { Width = 6 });
        add.Children.Add(UiKit.Btn("Combined 6-axis", () => AddFrom(HwF.Combined6Axis())));
        add.Children.Add(new Border { Width = 6 });
        add.Children.Add(UiKit.Btn("Wide range", () => AddFrom(HwF.WideRange())));
        s.Children.Add(add);

        s.Children.Add(new Border { Height = 14 });
        _layoutWarn.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        s.Children.Add(_layoutWarn);
        var detect = UiKit.Btn("Detect layout from received reports", () =>
        {
            var ok = _app.ApplyGuessedLayout();
            _app.Toast(ok ? "Layout updated from the reports received." : "Not enough data yet. Move the cap first.", !ok);
            RefreshAll();
        });
        detect.HorizontalAlignment = HorizontalAlignment.Left; detect.Margin = new Thickness(0, 8, 0, 0);
        s.Children.Add(detect);
        return UiKit.Card(s);
    }

    private void AddFrom(HardwareProfile template)
    {
        template.Id = Guid.NewGuid();
        template.Name = (InputDialog.Ask(Window.GetWindow(this), "New hardware profile", "Name:", template.Name)) ?? "";
        if (template.Name.Length == 0) return;
        foreach (var a in template.Axes) if (a.Range <= 0) a.Range = HwF.DefaultRange;
        _app.AddHardwareProfile(template);
    }

    private void RefreshProfiles()
    {
        _profileList.Children.Clear();
        foreach (var hp in _app.HardwareProfiles.ToList())
        {
            var p = hp;
            var active = p.Id == _app.ActiveHardware.Id;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(UiKit.T(p.Name, null, 13.5));
            ((TextBlock)title.Children[0]).FontWeight = FontWeights.SemiBold;
            if (active) { var t = UiKit.T("  · active", null, 12); t.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); title.Children.Add(t); }
            text.Children.Add(title);
            text.Children.Add(UiKit.T((p.Calibrated ? "Calibrated" : "Not calibrated") + (p.VendorId is int v && p.ProductId is int pid ? $" · {v:X4}:{pid:X4}" : ""), "Muted", 11.5));
            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            acts.Children.Add(IconBtn("", "Rename", () => { var n = InputDialog.Ask(Window.GetWindow(this), "Rename", "Name:", p.Name); if (n != null) { p.Name = n; _app.HardwareProfileEdited(p); RefreshProfiles(); } }));
            acts.Children.Add(IconBtn("", "Duplicate", () => { var c = p.Clone(); c.Id = Guid.NewGuid(); c.Name = p.Name + " (copy)"; _app.AddHardwareProfile(c); }));
            acts.Children.Add(IconBtn("", "Delete", () =>
            {
                if (_app.HardwareProfiles.Count <= 1) { _app.Toast("At least one hardware profile is required.", true); return; }
                if (MessageBox.Show($"Delete hardware profile '{p.Name}'?", "Space Mouse Studio", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) _app.RemoveHardwareProfile(p);
            }));
            Grid.SetColumn(acts, 1);
            g.Children.Add(text); g.Children.Add(acts);
            var row = new Border { Child = g, Padding = new Thickness(14, 10, 8, 10), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand };
            row.SetResourceReference(Border.BackgroundProperty, active ? "AccentSoftBrush" : "SurfaceAltBrush");
            row.SetResourceReference(Border.BorderBrushProperty, active ? "AccentBrush" : "BorderBrush");
            row.MouseLeftButtonUp += (_, e) => { if (!active && e.OriginalSource is not Button) _app.ApplyHardwareProfile(p); };
            _profileList.Children.Add(row);
        }
        var warn = _app.HasUnrecognisedReports;
        _layoutWarn.Text = warn ? "The device sends reports that the active profile cannot decode. Use 'Detect layout' or pick a matching template." : "";
        _layoutWarn.Visibility = warn ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Button IconBtn(string glyph, string tip, Action a)
    {
        var b = UiKit.Btn(glyph, a, "IconButton");
        b.ToolTip = tip;
        return b;
    }

    // ------------------------------------------------------------------ (c) manual configuration
    private UIElement BuildManual()
    {
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Manual configuration", "H2"));
        s.Children.Add(UiKit.T("This is the hardware calibration of the active hardware profile: which raw channel feeds which movement, its direction, its range and dead zone. How strongly a movement acts is set per profile on the Customize page (sensitivity) - the two are independent.", "Muted", 12.5, true));
        s.Children.Add(new Border { Height = 10 });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        foreach (var w in new[] { 170.0, 90, 80, 100, 0, 80 }) header.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        var heads = new[] { "Movement", "Channel", "Invert", "Range", "Dead zone", "Gain" };
        for (var i = 0; i < heads.Length; i++) { var t = UiKit.T(heads[i], "Muted", 11.5); Grid.SetColumn(t, i); header.Children.Add(t); }
        s.Children.Add(header);
        s.Children.Add(_axisRows);
        return UiKit.Card(s);
    }

    private void RebuildAxisRows()
    {
        var hp = _app.ActiveHardware;
        _shownProfile = hp.Id;
        _axisRows.Children.Clear();
        foreach (var a in AxisInfo.All)
        {
            var cal = hp.Axes[(int)a];
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            foreach (var w in new[] { 170.0, 90, 80, 100, 0, 80 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

            var name = UiKit.T(AxisInfo.Name(a)); name.VerticalAlignment = VerticalAlignment.Center;
            var ch = new ComboBox { ItemsSource = new[] { "1", "2", "3", "4", "5", "6" }, SelectedIndex = Math.Clamp(cal.Slot, 0, 5), Width = 70, HorizontalAlignment = HorizontalAlignment.Left };
            ch.SelectionChanged += (_, _) => { if (ch.SelectedIndex >= 0) { cal.Slot = ch.SelectedIndex; _app.HardwareProfileEdited(hp); } };
            var inv = new CheckBox { Style = (Style)Application.Current.Resources["Switch"], IsChecked = cal.Invert, VerticalAlignment = VerticalAlignment.Center };
            inv.Click += (_, _) => { cal.Invert = inv.IsChecked == true; _app.HardwareProfileEdited(hp); };
            var range = new TextBox { Text = cal.Range.ToString("0.##", CultureInfo.InvariantCulture), Width = 80, HorizontalAlignment = HorizontalAlignment.Left };
            range.LostFocus += (_, _) =>
            {
                if (TryNum(range.Text, out var v) && v >= 1) { cal.Range = v; _app.HardwareProfileEdited(hp); }
                range.Text = cal.Range.ToString("0.##", CultureInfo.InvariantCulture);
            };
            var dzText = UiKit.T($"{cal.Deadzone * 100:0.#} %", "Muted"); dzText.Width = 52; dzText.TextAlignment = TextAlignment.Right; dzText.VerticalAlignment = VerticalAlignment.Center;
            var dz = new Slider { Minimum = 0, Maximum = 0.5, Value = Math.Clamp(cal.Deadzone, 0, 0.5), VerticalAlignment = VerticalAlignment.Center };
            dz.ValueChanged += (_, _) => { cal.Deadzone = Math.Round(dz.Value, 3); dzText.Text = $"{cal.Deadzone * 100:0.#} %"; _app.HardwareProfileEdited(hp); };
            var dzCell = new DockPanel { Margin = new Thickness(0, 0, 14, 0) };
            DockPanel.SetDock(dzText, Dock.Right); dzCell.Children.Add(dzText); dzCell.Children.Add(dz);
            var gain = new TextBox { Text = cal.Gain.ToString("0.##", CultureInfo.InvariantCulture), Width = 64, HorizontalAlignment = HorizontalAlignment.Left };
            gain.LostFocus += (_, _) =>
            {
                if (TryNum(gain.Text, out var v) && v > 0 && v <= 10) { cal.Gain = v; _app.HardwareProfileEdited(hp); }
                gain.Text = cal.Gain.ToString("0.##", CultureInfo.InvariantCulture);
            };
            var cells = new UIElement[] { name, ch, inv, range, dzCell, gain };
            for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); g.Children.Add(cells[i]); }
            _axisRows.Children.Add(g);
        }
    }

    private static bool TryNum(string s, out double v)
        => double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    // ------------------------------------------------------------------ (d) live test + inspector
    private UIElement BuildLive()
    {
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Live test", "H2"));
        s.Children.Add(UiKit.T("Move the cap or the virtual controller. This shows the calibrated result exactly as the rest of the app sees it.", "Muted", 12.5, true));
        s.Children.Add(new Border { Height = 8 });
        s.Children.Add(_view);
        s.Children.Add(new Border { Height = 14 });
        s.Children.Add(UiKit.T("Report inspector", "H3"));
        s.Children.Add(UiKit.T("The newest raw HID reports (hex). Useful when reporting problems with a specific SpaceMouse model.", "Muted", 12, true));
        var box = new Border { Style = (Style)Application.Current.Resources["Inset"], Margin = new Thickness(0, 8, 0, 0), Child = _inspector };
        s.Children.Add(box);
        return UiKit.Card(s);
    }

    // ------------------------------------------------------------------ refresh
    private void RefreshAll()
    {
        RefreshConnection();
        RefreshProfiles();
        if (_shownProfile != _app.ActiveHardware.Id) RebuildAxisRows();
        else if (_axisRows.Children.Count == 0) RebuildAxisRows();
        _view.SetSummaries(
            a => { var c = _app.ActiveHardware.Axes[(int)a]; return $"Channel {c.Slot + 1}{(c.Invert ? " · inverted" : "")}"; },
            b => b == ButtonId.Left ? "Left button" : "Right button");
    }
}
