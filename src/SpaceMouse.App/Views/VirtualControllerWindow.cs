using System.Collections.Specialized;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Simulator;
using Shapes = System.Windows.Shapes;

namespace SpaceMouse.App.Views;

/// <summary>Draggable 2D (or 1D) pad that springs back to the centre on release unless "hold" is on. Y is up-positive.</summary>
internal sealed class Pad2D : Border
{
    private const double R = 14;
    private readonly Canvas _canvas = new();
    private readonly Shapes.Ellipse _thumb = new() { Width = 2 * R, Height = 2 * R };
    private readonly bool _useX, _useY;
    private readonly double _w, _h;

    public double X { get; private set; }
    public double Y { get; private set; }
    public bool Hold { get; set; }
    public event Action? Changed;

    public Pad2D(double w, double h, bool useX, bool useY)
    {
        _w = w; _h = h; _useX = useX; _useY = useY;
        Width = w; Height = h; CornerRadius = new CornerRadius(14); BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand; Background = Brushes.Transparent;
        this.SetResourceReference(BackgroundProperty, "SurfaceAltBrush");
        SetResourceReference(BorderBrushProperty, "BorderBrush");
        _canvas.Width = w - 2; _canvas.Height = h - 2; _canvas.ClipToBounds = true;
        // cross hair
        var hl = new Shapes.Line { X1 = 8, X2 = w - 10, Y1 = h / 2 - 1, Y2 = h / 2 - 1, StrokeThickness = 1, IsHitTestVisible = false, Opacity = useX ? 1 : 0.0 };
        var vl = new Shapes.Line { X1 = w / 2 - 1, X2 = w / 2 - 1, Y1 = 8, Y2 = h - 10, StrokeThickness = 1, IsHitTestVisible = false, Opacity = useY ? 1 : 0.0 };
        hl.SetResourceReference(Shapes.Shape.StrokeProperty, "BorderBrush");
        vl.SetResourceReference(Shapes.Shape.StrokeProperty, "BorderBrush");
        _thumb.SetResourceReference(Shapes.Shape.FillProperty, "AccentBrush");
        _thumb.IsHitTestVisible = false;
        _canvas.Children.Add(hl); _canvas.Children.Add(vl); _canvas.Children.Add(_thumb);
        Child = _canvas;
        Place();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); FromMouse(e); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) FromMouse(e); };
        MouseLeftButtonUp += (_, _) => { ReleaseMouseCapture(); if (!Hold) Reset(); };
        LostMouseCapture += (_, _) => { if (!Hold && (X != 0 || Y != 0)) Reset(); };
    }

    private void FromMouse(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var tx = Math.Max(1, _w / 2 - R - 4);
        var ty = Math.Max(1, _h / 2 - R - 4);
        Set(_useX ? (p.X - _w / 2) / tx : 0, _useY ? -(p.Y - _h / 2) / ty : 0);
    }

    public void Set(double x, double y)
    {
        X = Math.Clamp(x, -1, 1); Y = Math.Clamp(y, -1, 1);
        Place();
        Changed?.Invoke();
    }

    public void Reset() => Set(0, 0);

    private void Place()
    {
        var tx = Math.Max(1, _w / 2 - R - 4);
        var ty = Math.Max(1, _h / 2 - R - 4);
        Canvas.SetLeft(_thumb, _w / 2 - 1 - R + X * tx);
        Canvas.SetTop(_thumb, _h / 2 - 1 - R - Y * ty);
    }
}

/// <summary>Big press/hold button; in latch mode a click toggles it.</summary>
internal sealed class BigButton : Border
{
    private readonly TextBlock _text;
    public bool Latch { get; set; }
    public bool IsDown { get; private set; }
    public event Action<bool>? StateChanged;

    public BigButton(string label)
    {
        _text = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Child = _text; Height = 52; CornerRadius = new CornerRadius(14); BorderThickness = new Thickness(1); Cursor = Cursors.Hand;
        Visual();
        MouseLeftButtonDown += (_, e) =>
        {
            if (Latch) SetDown(!IsDown);
            else { CaptureMouse(); SetDown(true); }
            e.Handled = true;
        };
        MouseLeftButtonUp += (_, _) => { if (!Latch) { ReleaseMouseCapture(); SetDown(false); } };
        LostMouseCapture += (_, _) => { if (!Latch) SetDown(false); };
    }

    public void SetDown(bool down)
    {
        if (down == IsDown) return;
        IsDown = down; Visual(); StateChanged?.Invoke(down);
    }

    private void Visual()
    {
        SetResourceReference(BackgroundProperty, IsDown ? "AccentBrush" : "SurfaceAltBrush");
        SetResourceReference(BorderBrushProperty, IsDown ? "AccentBrush" : "BorderBrush");
        _text.SetResourceReference(TextBlock.ForegroundProperty, IsDown ? "OnAccentBrush" : "TextBrush");
    }
}

/// <summary>The virtual SpaceMouse: a clearly labelled simulation driving the same input pipeline as real hardware.</summary>
public sealed class VirtualControllerWindow : Window
{
    private static VirtualControllerWindow? _instance;
    private readonly AppController _app = AppController.Instance;
    private VirtualSpaceMouse V => _app.Virtual;

    private readonly Pad2D _padT = new(190, 190, true, true), _padR = new(190, 190, true, true), _padZ = new(64, 190, false, true), _padRz = new(190, 64, true, false);
    private readonly BigButton _left = new("Left button"), _right = new("Right button");
    private readonly Slider _strength = new() { Minimum = 10, Maximum = 100, Value = 100 };
    private readonly TextBlock _strengthText = UiKit.T("100 %", "Muted");
    private readonly CheckBox _useVirtual = new() { Style = (Style)Application.Current.Resources["Switch"], VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _sendToSystem = new() { Content = "Let it control Windows (otherwise actions are only simulated and logged)", Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _hold = new() { Content = "Hold position (pads stay where you leave them)", Margin = new Thickness(0, 10, 0, 0) };
    private readonly CheckBox _latch = new() { Content = "Latch buttons (click toggles)", Margin = new Thickness(0, 6, 0, 0) };
    private readonly CheckBox _top = new() { Content = "Keep this window on top", Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _layout = new() { Width = 220 };
    private readonly TextBox _range = new() { Width = 90, Text = "350" };
    private readonly Button _plug = new() { MinWidth = 150 };
    private readonly Border _chip = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _log = new();
    private readonly FrameworkElement _controls;
    private readonly HashSet<Key> _keys = new();
    private bool _plugged = true, _syncing;

    public static void ShowSingleton()
    {
        try
        {
            if (_instance == null) { _instance = new VirtualControllerWindow(); _instance.Closed += (_, _) => _instance = null; }
            if (!_instance.IsVisible) _instance.Show();
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
        catch (Exception e) { MessageBox.Show("Virtual controller could not be opened: " + e.Message, "Space Mouse Studio"); }
    }

    private VirtualControllerWindow()
    {
        Title = "Virtual SpaceMouse (simulation)";
        Width = 580; Height = 760; MinWidth = 520; MinHeight = 560;
        SetResourceReference(BackgroundProperty, "BgBrush");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - Width - 40; Top = SystemParameters.WorkArea.Top + 40;
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);

        // ---- banner
        var banner = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 14) };
        banner.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        banner.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        var bs = new StackPanel();
        bs.Children.Add(UiKit.T("SIMULATION - this is not your real SpaceMouse", "H3"));
        bs.Children.Add(UiKit.T("The virtual controller creates genuine HID reports and feeds them into the same pipeline as real hardware. It is meant for trying profiles and testing without a device.", "Muted", 12, true));
        banner.Child = bs;

        // ---- source row
        _useVirtual.Click += (_, _) => Safe(() => _app.UseVirtualController(_useVirtual.IsChecked == true));
        var srcRow = new Grid();
        srcRow.ColumnDefinitions.Add(new ColumnDefinition());
        srcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var srcText = new StackPanel();
        srcText.Children.Add(UiKit.T("Use virtual controller", "H3"));
        srcText.Children.Add(UiKit.T("Switches the whole app to the simulation. Turn off to return to your real device.", "Muted", 12, true));
        Grid.SetColumn(_useVirtual, 1);
        srcRow.Children.Add(srcText); srcRow.Children.Add(_useVirtual);
        var srcCard = new StackPanel();
        srcCard.Children.Add(srcRow);
        srcCard.Children.Add(new Border { Height = 10 });
        srcCard.Children.Add(_chip);
        _sendToSystem.Click += (_, _) => Safe(() => _app.SetVirtualSendsToSystem(_sendToSystem.IsChecked == true));
        srcCard.Children.Add(_sendToSystem);

        // ---- hardware variant
        _layout.ItemsSource = new[] { "3 reports (translation, rotation, buttons)", "Combined 6-axis report", "Wide range (16 bit)" };
        _layout.SelectedIndex = (int)V.Layout;
        _range.Text = V.Range.ToString("0");
        _layout.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            if (_layout.SelectedIndex == 2) _range.Text = "32767"; else if (_range.Text == "32767") _range.Text = "350";
            ApplyVariant();
        };
        _range.LostFocus += (_, _) => ApplyVariant();
        _range.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyVariant(); };
        var varRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        varRow.Children.Add(_layout);
        varRow.Children.Add(UiKit.T("Range  ", "Muted")); ((TextBlock)varRow.Children[1]).Margin = new Thickness(14, 8, 6, 0);
        varRow.Children.Add(_range);
        var variantCard = new StackPanel();
        variantCard.Children.Add(UiKit.T("Hardware variant", "H3"));
        variantCard.Children.Add(UiKit.T("Different SpaceMouse generations use different report layouts and value ranges. Pick one to test the app against it.", "Muted", 12, true));
        variantCard.Children.Add(varRow);

        // ---- options
        _strength.ValueChanged += (_, _) => { _strengthText.Text = $"{_strength.Value:0} %"; Push(); };
        var strRow = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        strRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        strRow.ColumnDefinitions.Add(new ColumnDefinition());
        strRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        strRow.Children.Add(UiKit.T("Strength", "Muted"));
        Grid.SetColumn(_strength, 1); Grid.SetColumn(_strengthText, 2); _strengthText.HorizontalAlignment = HorizontalAlignment.Right;
        strRow.Children.Add(_strength); strRow.Children.Add(_strengthText);
        _hold.Click += (_, _) => { foreach (var p in Pads) { p.Hold = _hold.IsChecked == true; if (!p.Hold) p.Reset(); } };
        _latch.Click += (_, _) => { _left.Latch = _right.Latch = _latch.IsChecked == true; if (!_left.Latch) { _left.SetDown(false); _right.SetDown(false); } };
        _top.Click += (_, _) => Topmost = _top.IsChecked == true;
        var optCard = new StackPanel();
        optCard.Children.Add(UiKit.T("Controls", "H3"));
        optCard.Children.Add(strRow); optCard.Children.Add(_hold); optCard.Children.Add(_latch); optCard.Children.Add(_top);

        // ---- pads
        foreach (var p in Pads) p.Changed += Push;
        _left.StateChanged += d => Safe(() => V.SetButton(ButtonId.Left, d));
        _right.StateChanged += d => Safe(() => V.SetButton(ButtonId.Right, d));

        var padsGrid = new Grid();
        padsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        padsGrid.ColumnDefinitions.Add(new ColumnDefinition());
        padsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        padsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        padsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tBox = Labeled("Slide / push  (X, Y)", _padT); var rBox = Labeled("Tilt  (RX, RY)", _padR);
        var zBox = Labeled("Lift (Z)", _padZ); var rzBox = Labeled("Twist (RZ)", _padRz);
        Grid.SetColumn(zBox, 2);
        Grid.SetRow(rBox, 1); Grid.SetColumn(rBox, 0);
        Grid.SetRow(rzBox, 1); Grid.SetColumn(rzBox, 1); Grid.SetColumnSpan(rzBox, 2); rzBox.Margin = new Thickness(18, 14, 0, 0);
        tBox.Margin = new Thickness(0, 0, 0, 0); rBox.Margin = new Thickness(0, 14, 0, 0);
        padsGrid.Children.Add(tBox); padsGrid.Children.Add(zBox); padsGrid.Children.Add(rBox); padsGrid.Children.Add(rzBox);

        var btnGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition());
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_right, 2);
        btnGrid.Children.Add(_left); btnGrid.Children.Add(_right);

        _plug.Click += (_, _) => Safe(() => { _plugged = !_plugged; V.SetConnected(_plugged); Sync(); });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        actions.Children.Add(UiKit.Btn("Center all", CenterAll));
        actions.Children.Add(new Border { Width = 8 });
        actions.Children.Add(_plug);

        var ctl = new StackPanel();
        ctl.Children.Add(padsGrid); ctl.Children.Add(btnGrid); ctl.Children.Add(actions);
        ctl.Children.Add(UiKit.T("Keyboard (while this window is focused): W A S D / arrows = slide & push, Q E = twist, R F = lift, Space = left button, C = right button.", "Muted", 11.5, true));
        ((TextBlock)ctl.Children[^1]).Margin = new Thickness(0, 12, 0, 0);
        _controls = ctl;

        // ---- activity
        var logCard = new StackPanel();
        logCard.Children.Add(UiKit.T("What the app just did", "H3"));
        _log.Margin = new Thickness(0, 8, 0, 0);
        logCard.Children.Add(_log);

        var page = new StackPanel { Margin = new Thickness(22, 20, 22, 22) };
        page.Children.Add(banner);
        page.Children.Add(UiKit.Card(srcCard, 16));
        page.Children.Add(UiKit.Card(variantCard, 16));
        page.Children.Add(UiKit.Card(optCard, 16));
        page.Children.Add(UiKit.Card(ctl, 16));
        page.Children.Add(UiKit.Card(logCard, 16));
        Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        PreviewKeyDown += OnKeyDown; PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => { _keys.Clear(); Push(); };
        _app.StateChanged += Sync;
        _app.Activity.CollectionChanged += OnActivity;
        Closed += OnClosed;
        _plugged = !(_app.UseVirtual && !V.IsConnected);
        Sync(); RebuildLog();
    }

    private IEnumerable<Pad2D> Pads => new[] { _padT, _padR, _padZ, _padRz };

    private static StackPanel Labeled(string label, UIElement el)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        s.Children.Add(UiKit.T(label, "Muted", 11.5));
        ((TextBlock)s.Children[0]).Margin = new Thickness(2, 0, 0, 5);
        s.Children.Add(el);
        return s;
    }

    private static void Safe(Action a) { try { a(); } catch (Exception e) { AppController.Instance.Log("Virtual controller: " + e.Message, true); } }

    // ---------------------------------------------------------------- output
    private static double Clamp(double v) => Math.Clamp(v, -1, 1);

    private void Push()
    {
        Safe(() =>
        {
            var s = _strength.Value / 100.0;
            double kx = 0, ky = 0, kz = 0, krz = 0;
            if (_keys.Contains(Key.D) || _keys.Contains(Key.Right)) kx += 1;
            if (_keys.Contains(Key.A) || _keys.Contains(Key.Left)) kx -= 1;
            if (_keys.Contains(Key.W) || _keys.Contains(Key.Up)) ky += 1;
            if (_keys.Contains(Key.S) || _keys.Contains(Key.Down)) ky -= 1;
            if (_keys.Contains(Key.R)) kz += 1;
            if (_keys.Contains(Key.F)) kz -= 1;
            if (_keys.Contains(Key.E)) krz += 1;
            if (_keys.Contains(Key.Q)) krz -= 1;
            // RX = tilt forward (pad up), RY = tilt right (pad right)
            V.SetAxes(Clamp(_padT.X * s + kx * s), Clamp(_padT.Y * s + ky * s), Clamp(_padZ.Y * s + kz * s),
                      Clamp(_padR.Y * s), Clamp(_padR.X * s), Clamp(_padRz.X * s + krz * s));
        });
    }

    private void CenterAll()
    {
        foreach (var p in Pads) p.Reset();
        _keys.Clear();
        _left.SetDown(false); _right.SetDown(false);
        Safe(V.Center);
    }

    private void ApplyVariant()
    {
        if (_syncing) return;
        Safe(() =>
        {
            if (!double.TryParse(_range.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r)) r = V.Range;
            r = Math.Clamp(Math.Round(r), 10, 32767);
            _range.Text = r.ToString("0");
            _app.ConfigureVirtual((VirtualLayout)Math.Max(0, _layout.SelectedIndex), r);
        });
    }

    // ---------------------------------------------------------------- keyboard
    private static bool TypingInText() => Keyboard.FocusedElement is TextBox;

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (TypingInText()) return;
        var k = e.Key;
        if (k == Key.Space) { if (!_left.Latch) _left.SetDown(true); else if (!e.IsRepeat) _left.SetDown(!_left.IsDown); e.Handled = true; return; }
        if (k == Key.C) { if (!_right.Latch) _right.SetDown(true); else if (!e.IsRepeat) _right.SetDown(!_right.IsDown); e.Handled = true; return; }
        if (IsAxisKey(k)) { if (_keys.Add(k)) Push(); e.Handled = true; }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var k = e.Key;
        if (k == Key.Space && !_left.Latch) { _left.SetDown(false); e.Handled = true; return; }
        if (k == Key.C && !_right.Latch) { _right.SetDown(false); e.Handled = true; return; }
        if (_keys.Remove(k)) { Push(); e.Handled = true; }
    }

    private static bool IsAxisKey(Key k) => k is Key.W or Key.A or Key.S or Key.D or Key.Up or Key.Down or Key.Left or Key.Right or Key.Q or Key.E or Key.R or Key.F;

    // ---------------------------------------------------------------- state
    private void Sync()
    {
        _syncing = true;
        try
        {
            _useVirtual.IsChecked = _app.UseVirtual;
            _sendToSystem.IsChecked = _app.Settings.VirtualControllerSendsToSystem;
            _controls.IsEnabled = _app.UseVirtual;
            _controls.Opacity = _app.UseVirtual ? 1 : 0.5;
            _plug.Content = _plugged ? "Unplug (simulate)" : "Plug in again";
            var kind = _app.ActiveSource;
            _chip.Child = UiKit.Pill(_app.SourceBadge + " · " + _app.SourceTitle, kind == SourceKind.Hardware ? "SuccessBrush" : kind == SourceKind.Virtual ? "AccentBrush" : "WarnBrush");
            _layout.SelectedIndex = (int)V.Layout;
            if (!_range.IsKeyboardFocused) _range.Text = V.Range.ToString("0");
        }
        finally { _syncing = false; }
    }

    private void OnActivity(object? s, NotifyCollectionChangedEventArgs e) => RebuildLog();

    private void RebuildLog()
    {
        _log.Children.Clear();
        foreach (var a in _app.Activity.Take(12))
        {
            var row = new TextBlock { Style = (Style)Application.Current.Resources["Mono"], Margin = new Thickness(0, 2, 0, 2), TextTrimming = TextTrimming.CharacterEllipsis, Text = $"{a.TimeText}  {a.Text}" };
            row.SetResourceReference(TextBlock.ForegroundProperty, a.IsError ? "DangerBrush" : "TextBrush");
            _log.Children.Add(row);
        }
        if (_log.Children.Count == 0) _log.Children.Add(UiKit.T("Nothing yet. Switch on SpaceMouse control and move the pads.", "Muted", 12, true));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _app.StateChanged -= Sync;
        _app.Activity.CollectionChanged -= OnActivity;
        // never leave the simulation deflected or with a button held
        try { foreach (var p in Pads) { p.Hold = false; p.Reset(); } } catch { }
        try { _left.Latch = _right.Latch = false; _left.SetDown(false); _right.SetDown(false); } catch { }
        try { V.SetButton(ButtonId.Left, false); V.SetButton(ButtonId.Right, false); V.Center(); } catch { }
    }
}
