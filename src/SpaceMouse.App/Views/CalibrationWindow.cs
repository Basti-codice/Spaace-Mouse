using System.Windows.Threading;
using SpaceMouse.App.Controls;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Calibration;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using Shapes = System.Windows.Shapes;

namespace SpaceMouse.App.Views;

/// <summary>Small helpers to build the code-only views quickly (shared by the Device page and the virtual controller).</summary>
internal static class UiKit
{
    public static TextBlock T(string text, string? style = null, double size = 0, bool wrap = false)
    {
        var t = new TextBlock { Text = text };
        if (style != null) t.Style = (Style)Application.Current.Resources[style];
        if (size > 0) t.FontSize = size;
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static Border Card(UIElement child, double pad = 20)
        => new() { Style = (Style)Application.Current.Resources["Card"], Padding = new Thickness(pad), Child = child, Margin = new Thickness(0, 0, 0, 16) };

    public static Button Btn(string text, Action click, string? style = null)
    {
        var b = new Button { Content = text };
        if (style != null) b.Style = (Style)Application.Current.Resources[style];
        b.Click += (_, _) => { try { click(); } catch (Exception e) { MessageBox.Show(e.Message, "Space Mouse Studio"); } };
        return b;
    }

    public static Border Pill(string text, string brushKey)
    {
        var dot = new Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shapes.Shape.FillProperty, brushKey);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
        var b = new Border { Child = row, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        return b;
    }

    /// <summary>Bidirectional bar around a centre line, value -1..1.</summary>
    public sealed class BiBar : Grid
    {
        private readonly ScaleTransform _neg = new(0, 1), _pos = new(0, 1);
        public BiBar()
        {
            Height = 6;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition());
            var bg = new Border { CornerRadius = new CornerRadius(3) };
            bg.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");
            SetColumnSpan(bg, 2);
            var n = new Border { CornerRadius = new CornerRadius(3), RenderTransformOrigin = new Point(1, 0.5), RenderTransform = _neg };
            var p = new Border { CornerRadius = new CornerRadius(3), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _pos };
            n.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            p.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            SetColumn(p, 1);
            Children.Add(bg); Children.Add(n); Children.Add(p);
        }
        public void Update(double v)
        {
            v = Math.Clamp(v, -1, 1);
            _neg.ScaleX = Math.Max(0, -v);
            _pos.ScaleX = Math.Max(0, v);
        }
    }
}

/// <summary>Minimal modal text prompt.</summary>
internal sealed class InputDialog : Window
{
    private readonly TextBox _box = new();
    public string Result => _box.Text.Trim();

    public InputDialog(Window? owner, string title, string prompt, string initial)
    {
        Title = title; Width = 400; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner != null && owner.IsVisible) Owner = owner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        _box.Text = initial; _box.Margin = new Thickness(0, 8, 0, 16);
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.Resources["PrimaryButton"], Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) => { if (Result.Length > 0) DialogResult = true; };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        var st = new StackPanel { Margin = new Thickness(20) };
        st.Children.Add(UiKit.T(prompt)); st.Children.Add(_box); st.Children.Add(btns);
        Content = st;
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(Window? owner, string title, string prompt, string initial)
    {
        var d = new InputDialog(owner, title, prompt, initial);
        return d.ShowDialog() == true ? d.Result : null;
    }
}

/// <summary>
/// Hardware calibration wizard. Works on the raw channel values of the active input source (real device or virtual
/// controller); it never invents data - without a connected source the wizard cannot proceed.
/// </summary>
public sealed class CalibrationWindow : Window
{
    private const int RestTarget = 90;           // ~1.5 s at 60 Hz
    private readonly AppController _app = AppController.Instance;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly ContentControl _host = new();
    private readonly TextBlock _stepLabel = new();
    private readonly Button _back = new(), _next = new(), _cancel = new();
    private readonly ProgressBar _progress = new();
    private CalibrationSession _session;
    private HardwareProfile? _profile;
    private int _page;                            // 0..6
    private long _reports;
    private string _message = "";
    private bool _messageIsWarning;

    // per-page widgets
    private TextBlock? _detName, _detUsb, _detState, _detNote, _restInfo, _dirTitle, _dirSub, _dirArrow, _dirMsg, _reviewProblems, _saveInfo;
    private Border? _detPill;
    private ProgressBar? _restBar, _dirBar;
    private readonly UiKit.BiBar?[] _rawBars = new UiKit.BiBar?[6];
    private readonly UiKit.BiBar?[] _checkBars = new UiKit.BiBar?[6];
    private SpaceMouseView? _hint, _testView;
    private TextBox? _nameBox;
    private int _dirStepShown = -1;
    private readonly bool _resume;

    public CalibrationWindow()
    {
        Title = "Calibrate SpaceMouse";
        Width = 780; Height = 680; MinWidth = 680; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "BgBrush");
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        _session = NewSession();
        _resume = _app.IsActive;
        if (_resume) _app.Engine.SetMode(SpaceMouse.Core.Engine.EngineMode.Standard);   // never fire real actions while calibrating

        _stepLabel.Style = (Style)Application.Current.Resources["Muted"];
        _progress.Style = (Style)Application.Current.Resources["ThinBar"]; _progress.Maximum = 7; _progress.Margin = new Thickness(0, 8, 0, 0);

        _back.Content = "Back"; _next.Content = "Next"; _cancel.Content = "Cancel";
        _next.Style = (Style)Application.Current.Resources["PrimaryButton"];
        _back.Margin = new Thickness(0, 0, 8, 0); _next.MinWidth = 110; _cancel.Margin = new Thickness(0, 0, 8, 0);
        _back.Click += (_, _) => Safe(OnBack); _next.Click += (_, _) => Safe(OnNext); _cancel.Click += (_, _) => Close();

        var head = new StackPanel { Margin = new Thickness(28, 22, 28, 8) };
        head.Children.Add(UiKit.T("Hardware calibration", "H1"));
        head.Children.Add(_stepLabel); head.Children.Add(_progress);
        var foot = new Grid { Margin = new Thickness(28, 8, 28, 20) };
        foot.ColumnDefinitions.Add(new ColumnDefinition());
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(_cancel); right.Children.Add(_back); right.Children.Add(_next);
        Grid.SetColumn(right, 1); foot.Children.Add(right);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _host.Margin = new Thickness(28, 8, 28, 8);
        Grid.SetRow(_host, 1); Grid.SetRow(foot, 2);
        root.Children.Add(head); root.Children.Add(_host); root.Children.Add(foot);
        Content = root;

        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        _app.Hub.RawReceived += OnRaw;
        _app.StateChanged += OnState;
        _timer.Tick += (_, _) => Safe(Tick);
        _timer.Start();
        Closed += (_, _) => { if (_resume) _app.Engine.SetMode(SpaceMouse.Core.Engine.EngineMode.Custom); _timer.Stop(); _app.Hub.RawReceived -= OnRaw; _app.StateChanged -= OnState; };
        ShowPage(0);
    }

    public static void Run(Window? owner)
    {
        try
        {
            var w = new CalibrationWindow();
            if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // Modeless on purpose: the virtual controller window must stay usable while calibrating the simulation.
            if (owner != null) owner.IsEnabled = false;
            w.Closed += (_, _) => { if (owner != null) { owner.IsEnabled = true; owner.Activate(); } };
            w.Show();
        }
        catch (Exception e) { MessageBox.Show("Calibration could not be opened: " + e.Message, "Space Mouse Studio"); }
    }

    private CalibrationSession NewSession() => new(_app.Hub.Profile);

    private void Safe(Action a) { try { a(); } catch (Exception e) { _message = e.Message; _messageIsWarning = true; } }
    private void OnRaw(RawReport r) => System.Threading.Interlocked.Increment(ref _reports);
    private void OnState() { if (_page == 0) UpdateDetect(); }

    // ------------------------------------------------------------------ navigation
    private static readonly string[] Titles =
        { "Find your device", "Resting position", "Move in every direction", "Check directions", "Value ranges", "Try it out", "Save" };

    private void ShowPage(int p)
    {
        _page = p;
        _message = ""; _messageIsWarning = false; _dirStepShown = -1;
        _stepLabel.Text = $"Step {p + 1} of 7 · {Titles[p]}";
        _progress.Value = p + 1;
        _back.Visibility = p == 0 ? Visibility.Collapsed : Visibility.Visible;
        _next.Content = p == 6 ? "Save profile" : "Next";
        _host.Content = p switch { 0 => BuildDetect(), 1 => BuildRest(), 2 => BuildDirections(), 3 => BuildReview(), 4 => BuildRanges(), 5 => BuildTest(), _ => BuildSave() };
        if (p == 1) _session.RedoCurrent();
        Tick();
    }

    private void OnBack()
    {
        // The direction measurements cannot be re-entered half way: going back from them restarts from the resting step.
        if (_page == 1) ShowPage(0);
        else if (_page == 2 || _page == 3) { _session = NewSession(); _profile = null; ShowPage(1); }
        else if (_page >= 4) ShowPage(_page - 1);
    }

    private void Restart() { _session = NewSession(); _profile = null; ShowPage(0); }

    private void OnNext()
    {
        switch (_page)
        {
            case 0: if (_app.ActiveSource != SourceKind.None) ShowPage(1); break;
            case 2:
                if (_session.Finished) { Finish(); break; }
                var r = _session.Complete();
                _message = r.Message; _messageIsWarning = !r.Success;
                if (_session.Finished) Finish();
                break;
            case 3: ShowPage(4); break;
            case 4: ShowPage(5); break;
            case 5: ShowPage(6); break;
            case 6: Save(); break;
        }
    }

    private void Finish()
    {
        _profile = _session.Build(SuggestName());
        ShowPage(3);
    }

    private string SuggestName()
    {
        var d = _app.Hub.Device;
        var baseName = _app.ActiveSource == SourceKind.Virtual ? "Virtual controller" : d?.Name ?? "SpaceMouse";
        return baseName + " (calibrated)";
    }

    // ------------------------------------------------------------------ per-tick work
    private void Tick()
    {
        switch (_page)
        {
            case 0: UpdateDetect(); _next.IsEnabled = _app.ActiveSource != SourceKind.None; break;
            case 1: TickRest(); break;
            case 2: TickDirections(); break;
            case 3: TickCheck(); break;
            case 4: _next.IsEnabled = true; break;
            case 5: TickTest(); _next.IsEnabled = true; break;
            case 6: _next.IsEnabled = _nameBox != null && _nameBox.Text.Trim().Length > 0; break;
        }
    }

    // ---- page 0
    private UIElement BuildDetect()
    {
        _detName = UiKit.T("", "H2"); _detUsb = UiKit.T("", "Muted"); _detState = UiKit.T("", null, 13, true); _detNote = UiKit.T("", "Muted", 12.5, true);
        _detPill = new Border();
        var st = new StackPanel();
        st.Children.Add(_detPill); st.Children.Add(new Border { Height = 12 });
        st.Children.Add(_detName); st.Children.Add(_detUsb);
        st.Children.Add(new Border { Height = 10 }); st.Children.Add(_detState);
        var s = new StackPanel();
        s.Children.Add(UiKit.T("First, let's make sure the app can see your SpaceMouse. Calibration teaches the app which raw channel is which movement and in which direction.", null, 13, true));
        s.Children.Add(new Border { Height = 14 });
        s.Children.Add(UiKit.Card(st));
        s.Children.Add(_detNote);
        return s;
    }

    private void UpdateDetect()
    {
        if (_detName == null || _detUsb == null || _detState == null || _detNote == null || _detPill == null) return;
        var kind = _app.ActiveSource;
        _detName.Text = _app.SourceTitle;
        _detUsb.Text = kind == SourceKind.Hardware && _app.Hub.Device is { } d ? $"USB {d.UsbId}" + (string.IsNullOrEmpty(d.Manufacturer) ? "" : " · " + d.Manufacturer) : _app.SourceDetail;
        _detPill.Child = UiKit.Pill(_app.SourceBadge, kind == SourceKind.Hardware ? "SuccessBrush" : kind == SourceKind.Virtual ? "AccentBrush" : "WarnBrush");
        _detState.Text = kind switch
        {
            SourceKind.Hardware => "Connected. Reports received so far: " + System.Threading.Interlocked.Read(ref _reports),
            SourceKind.Virtual => "Connected (simulation).",
            _ => "Not connected.",
        };
        _detNote.Text = kind switch
        {
            SourceKind.Virtual => "You are calibrating the virtual controller, not a real device. Use the pads in the virtual controller window to move it.",
            SourceKind.None => "No device found. Plug in your SpaceMouse via USB, or open the virtual controller (Device page) to try the wizard without hardware. If it still does not show up, try 'Direct HID' as input method on the Device page.",
            _ => "Tip: keep your hand off the cap until the next step asks you to move it.",
        };
    }

    // ---- page 1
    private UIElement BuildRest()
    {
        _restBar = new ProgressBar { Style = (Style)Application.Current.Resources["ThinBar"], Maximum = RestTarget, Margin = new Thickness(0, 14, 0, 0) };
        _restInfo = UiKit.T("", "Muted", 12.5, true);
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(UiKit.T("Let go of the cap", "H2", 22));
        s.Children.Add(new Border { Height = 8 });
        s.Children.Add(UiKit.T("Take your hand off the SpaceMouse. The resting position and the noise of the sensor are measured now.", null, 13.5, true));
        s.Children.Add(_restBar); s.Children.Add(new Border { Height = 8 }); s.Children.Add(_restInfo);
        return s;
    }

    private void TickRest()
    {
        _next.IsEnabled = false;
        if (_restBar == null || _restInfo == null) return;
        if (System.Threading.Interlocked.Read(ref _reports) == 0)
        {
            _restInfo.Text = "Waiting for data from the device... If nothing happens, touch the cap once and let go.";
            _session.RedoCurrent();
            _restBar.Value = 0;
            return;
        }
        _session.Feed(_app.Hub.RawSlots);
        _restBar.Value = _session.RestSamples;
        _restInfo.Text = $"Measuring... {Math.Min(100, _session.RestSamples * 100 / RestTarget)} %";
        if (_session.RestSamples >= RestTarget)
        {
            var r = _session.Complete();
            if (r.Success) ShowPage(2);
            else { _restInfo.Text = r.Message; _session.RedoCurrent(); }
        }
    }

    // ---- page 2
    private UIElement BuildDirections()
    {
        _dirTitle = UiKit.T("", "H2", 24); _dirSub = UiKit.T("", "Muted", 13, true); _dirMsg = UiKit.T("", null, 12.5, true);
        _dirArrow = new TextBlock { FontSize = 56, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _dirArrow.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        _dirArrow.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(700))
            { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        _dirBar = new ProgressBar { Style = (Style)Application.Current.Resources["ThinBar"], Maximum = _session.Steps.Count - 1, Margin = new Thickness(0, 6, 0, 10) };
        _hint = new SpaceMouseView { Interactive = false, MaxHeight = 280 };

        var bars = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        bars.Children.Add(UiKit.T("Raw channels (movement from resting position)", "Muted", 11.5));
        for (var i = 0; i < 6; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(UiKit.T("Ch " + (i + 1), "Muted", 11.5));
            var bar = new UiKit.BiBar { VerticalAlignment = VerticalAlignment.Center };
            _rawBars[i] = bar;
            Grid.SetColumn(bar, 1); row.Children.Add(bar);
            bars.Children.Add(row);
        }

        var left = new StackPanel();
        left.Children.Add(_dirBar); left.Children.Add(_dirTitle); left.Children.Add(_dirSub);
        left.Children.Add(new Border { Height = 6 }); left.Children.Add(_dirArrow);
        left.Children.Add(_dirMsg); left.Children.Add(bars);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        btns.Children.Add(UiKit.Btn("Redo", () => { _session.RedoCurrent(); _message = "Try again."; _messageIsWarning = false; }));
        btns.Children.Add(new Border { Width = 8 });
        btns.Children.Add(UiKit.Btn("Skip", () => { _session.Skip(); if (_session.Finished) Finish(); }));
        left.Children.Add(btns);

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_hint, 1);
        g.Children.Add(left); g.Children.Add(_hint);
        return g;
    }

    public static string ArrowFor(Axis a, int sign) => (a, sign) switch
    {
        (Axis.Tx, > 0) => "→", (Axis.Tx, _) => "←",
        (Axis.Ty, > 0) => "↑", (Axis.Ty, _) => "↓",
        (Axis.Tz, > 0) => "⇧", (Axis.Tz, _) => "⇩",
        (Axis.Rx, > 0) => "⤴", (Axis.Rx, _) => "⤵",
        (Axis.Ry, > 0) => "↷", (Axis.Ry, _) => "↶",
        (_, > 0) => "↻", _ => "↺",
    };

    private void TickDirections()
    {
        if (_dirTitle == null || _dirSub == null || _dirArrow == null || _dirMsg == null || _dirBar == null) return;
        if (_session.Finished) { Finish(); return; }
        _session.Feed(_app.Hub.RawSlots);
        var step = _session.Current!;
        if (step.Kind == StepKind.Rest) { _session.Skip(); return; }
        if (_dirStepShown != _session.Index)
        {
            _dirStepShown = _session.Index;
            _dirTitle.Text = step.Title;
            _dirSub.Text = $"{AxisInfo.Name(step.Axis)} · {step.Hint}";
            _dirArrow.Text = ArrowFor(step.Axis, step.Sign);
            _hint?.SelectTarget(step.Axis.ToString());
        }
        _dirBar.Value = _session.Index - 1;
        _stepLabel.Text = $"Step 3 of 7 · Movement {Math.Min(_session.Index, _session.Steps.Count - 1)} of {_session.Steps.Count - 1}";
        var live = _session.LiveDeviation;
        for (var i = 0; i < 6; i++) _rawBars[i]?.Update(live[i] / Math.Max(1, _session.AssumedRange));
        if (_session.ReadyToAdvance)
        {
            var r = _session.Complete();
            _message = r.Message; _messageIsWarning = !r.Success;
            if (_session.Finished) { Finish(); return; }
        }
        _dirMsg.Text = _message;
        _dirMsg.SetResourceReference(TextBlock.ForegroundProperty, _messageIsWarning ? "WarnBrush" : "MutedBrush");
        _next.IsEnabled = true;
    }

    // ---- page 3
    private UIElement BuildReview()
    {
        var s = new StackPanel();
        s.Children.Add(UiKit.T("This is what was detected. If a movement works the wrong way round, flip it here and check with the live bars (move the cap).", null, 13, true));
        s.Children.Add(new Border { Height = 10 });
        var grid = new StackPanel();
        if (_profile != null)
        {
            var prof = _profile;
            foreach (var row in MappingRows(prof))
            {
                var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var name = UiKit.T(row.Name); var ch = UiKit.T("Channel " + row.Channel, "Muted");
                var inv = new CheckBox { Style = (Style)Application.Current.Resources["Switch"], IsChecked = row.Inverted, ToolTip = "Reverse this movement", HorizontalAlignment = HorizontalAlignment.Left };
                var idx = row.Index;
                inv.Click += (_, _) => { prof.Axes[idx].Invert = inv.IsChecked == true; };
                var bar = new UiKit.BiBar { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                _checkBars[idx] = bar;
                Grid.SetColumn(ch, 1); Grid.SetColumn(inv, 2); Grid.SetColumn(bar, 3);
                g.Children.Add(name); g.Children.Add(ch); g.Children.Add(inv); g.Children.Add(bar);
                grid.Children.Add(g);
            }
        }
        s.Children.Add(UiKit.Card(grid, 14));
        _reviewProblems = UiKit.T("", null, 12.5, true);
        _reviewProblems.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        s.Children.Add(_reviewProblems);
        s.Children.Add(new Border { Height = 8 });
        var redo = UiKit.Btn("Redo calibration from the start", Restart);
        redo.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(redo);
        var problems = _session.Validate();
        _reviewProblems.Text = problems.Count == 0 ? "" : "Some things need attention:\n• " + string.Join("\n• ", problems);
        return new ScrollViewer { Content = s, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public sealed record MappingRow(int Index, string Name, int Channel, bool Inverted);

    /// <summary>Friendly mapping table: logical axis -> raw channel (1-based) and inversion.</summary>
    public static List<MappingRow> MappingRows(HardwareProfile p)
        => AxisInfo.All.Select(a => new MappingRow((int)a, AxisInfo.Name(a), p.Axes[(int)a].Slot + 1, p.Axes[(int)a].Invert)).ToList();

    private void TickCheck()
    {
        _next.IsEnabled = true;
        if (_profile == null) return;
        var slots = _app.Hub.RawSlots;
        for (var i = 0; i < 6; i++) _checkBars[i]?.Update(ReportDecoder.Normalize(_profile.Axes[i], slots));
    }

    // ---- page 4
    private UIElement BuildRanges()
    {
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Measured from your strongest movements. Range is how far the cap travels in raw units; the dead zone ignores tiny noise around the centre.", null, 13, true));
        s.Children.Add(new Border { Height = 10 });
        var list = new StackPanel();
        var weak = false;
        if (_profile != null)
            foreach (var a in AxisInfo.All)
            {
                var c = _profile.Axes[(int)a];
                var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                var low = c.Range < 150; weak |= low;
                var rng = UiKit.T($"Range {c.Range:0}"); if (low) rng.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
                var dz = UiKit.T($"Dead zone {c.Deadzone * 100:0.#} %", "Muted");
                Grid.SetColumn(rng, 1); Grid.SetColumn(dz, 2);
                g.Children.Add(UiKit.T(AxisInfo.Name(a))); g.Children.Add(rng); g.Children.Add(dz);
                list.Children.Add(g);
            }
        s.Children.Add(UiKit.Card(list, 14));
        s.Children.Add(UiKit.T(weak ? "Some ranges look small. Push or twist the cap all the way to the stop during the movements, then use 'Redo'." : "These values look healthy.", "Muted", 12.5, true));
        s.Children.Add(new Border { Height = 8 });
        var redo = UiKit.Btn("Redo calibration from the start", Restart);
        redo.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(redo);
        return s;
    }

    // ---- page 5
    private UIElement BuildTest()
    {
        _testView = new SpaceMouseView { Interactive = false };
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Move the cap in every direction and check that the display follows. Buttons light up when pressed.", null, 13, true));
        s.Children.Add(new Border { Height = 10 });
        s.Children.Add(_testView);
        return s;
    }

    private void TickTest()
    {
        if (_profile == null || _testView == null) return;
        var slots = _app.Hub.RawSlots;
        var axes = new double[6];
        for (var i = 0; i < 6; i++) axes[i] = ReportDecoder.Normalize(_profile.Axes[i], slots);
        var cur = _app.Hub.Current;
        _testView.UpdateLive(new InputFrame(axes, cur.Left, cur.Right));
    }

    // ---- page 6
    private UIElement BuildSave()
    {
        _nameBox = new TextBox { Text = _profile?.Name ?? SuggestName(), Margin = new Thickness(0, 8, 0, 12) };
        _saveInfo = UiKit.T("", "Muted", 12.5, true);
        var problems = _session.Validate();
        _saveInfo.Text = problems.Count == 0
            ? "Your previous hardware profile stays available; you can switch between profiles on the Device page."
            : "The calibration is incomplete, so the profile will be saved as 'not calibrated':\n• " + string.Join("\n• ", problems);
        if (_app.ActiveSource == SourceKind.Virtual) _saveInfo.Text += "\nNote: this calibration describes the virtual controller.";
        var s = new StackPanel();
        s.Children.Add(UiKit.T("Name this hardware profile", "H2"));
        s.Children.Add(_nameBox); s.Children.Add(_saveInfo);
        return s;
    }

    private void Save()
    {
        if (_profile == null || _nameBox == null) return;
        _profile.Name = _nameBox.Text.Trim();
        _profile.Calibrated = _session.Validate().Count == 0;
        if (_app.ActiveSource == SourceKind.Hardware && _app.Hub.Device is { } d && d.VendorId != 0) { _profile.VendorId = d.VendorId; _profile.ProductId = d.ProductId; }
        _app.AddHardwareProfile(_profile);
        _app.ApplyHardwareProfile(_profile);
        _app.Toast("Hardware profile saved: " + _profile.Name, false);
        Close();
    }
}
