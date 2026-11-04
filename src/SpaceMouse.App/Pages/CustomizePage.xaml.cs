using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Threading;
using SpaceMouse.App.Controls;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Engine;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Profiles;
using Polyline = System.Windows.Shapes.Polyline;

namespace SpaceMouse.App.Pages;

public partial class CustomizePage : UserControl, IPage
{
    /// <summary>Set by other pages before navigating here ("Tx".."Rz", "Left", "Right").</summary>
    public static string? PendingTarget;

    private readonly AppController _app = AppController.Instance;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private string _target = "Tx";
    private ButtonModifier _modifier = ButtonModifier.None;
    private Profile? _profile;
    private bool _syncing, _tuningOpen;
    private Action<double>? _previewUpdate;

    public CustomizePage()
    {
        InitializeComponent();
        ProfileBox.ItemsSource = _app.Profiles;
        Device.TargetClicked += t => { _target = t; _modifier = ButtonModifier.None; BuildEditor(); };
        _timer.Tick += (_, _) =>
        {
            var f = _app.Hub.Current;
            Device.UpdateLive(f);
            if (Enum.TryParse<Axis>(_target, out var a)) _previewUpdate?.Invoke(f[a]);
        };
        Loaded += (_, _) => { _app.StateChanged += OnState; _timer.Start(); };
        Unloaded += (_, _) => { _app.StateChanged -= OnState; _timer.Stop(); };
    }

    public void OnShown()
    {
        if (PendingTarget != null) { _target = PendingTarget; _modifier = ButtonModifier.None; PendingTarget = null; }
        SyncProfile(force: true);
    }

    private void OnState() => SyncProfile(force: false);

    private void SyncProfile(bool force)
    {
        var p = _app.ActiveProfile;
        if (!force && ReferenceEquals(p, _profile)) { RefreshSummaries(); return; }
        _profile = p;
        _syncing = true;
        ProfileBox.SelectedItem = _app.Profiles.FirstOrDefault(x => x.Id == p?.Id);
        _syncing = false;
        PresetNote.Text = p?.PresetKey != null ? "Preset - changes can be reset on the Profiles page" : "";
        BuildEditor();
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ProfileBox.SelectedItem is not Profile p) return;
        if (_app.ActiveProfile?.Id != p.Id) _app.ActivateProfile(p);
    }

    private void RefreshSummaries()
    {
        Device.SetSummaries(a => ProfileSummary.AxisText(_profile, a), b => ProfileSummary.Button(_profile, b));
        BuildCombos();
    }

    private void BuildCombos()
    {
        var list = ProfileSummary.Combinations(_profile).ToList();
        NoCombos.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ComboList.Items.Clear();
        foreach (var (target, mod, text) in list)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8), FontSize = 12.5, Padding = new Thickness(12, 7, 12, 7) };
            var t = target; var m = mod;
            b.Click += (_, _) => { _target = t; _modifier = m; BuildEditor(); };
            ComboList.Items.Add(b);
        }
    }

    private void Edited() { if (_profile != null) _app.ProfileEdited(_profile); RefreshSummaries(); }

    // ================================================================ editor
    private void BuildEditor()
    {
        _previewUpdate = null;
        Editor.Children.Clear();
        RefreshSummaries();
        Device.SelectTarget(_target);
        if (_profile == null) { Editor.Children.Add(Text("No profile selected.", muted: true)); return; }

        if (Enum.TryParse<Axis>(_target, out var axis)) BuildAxisEditor(axis);
        else if (Enum.TryParse<ButtonId>(_target, out var btn)) BuildButtonEditor(btn);
    }

    private void BuildAxisEditor(Axis axis)
    {
        var p = _profile!;
        // --- header
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(Badge(AxisInfo.Short(axis)));
        head.Children.Add(new TextBlock { Text = AxisInfo.Name(axis), Style = (Style)FindResource("H2"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
        Editor.Children.Add(head);
        Editor.Children.Add(Text($"+  {AxisInfo.Positive(axis)}      −  {AxisInfo.Negative(axis)}", muted: true));

        // --- modifier selector
        Editor.Children.Add(Section("When"));
        var mods = new[] { ButtonModifier.None, ButtonModifier.Left, ButtonModifier.Right, ButtonModifier.Both };
        var names = new[] { "Always", "Left held", "Right held", "Both held" };
        var seg = Segmented(names, Array.IndexOf(mods, _modifier), i =>
        {
            _modifier = mods[i]; BuildEditor();
        }, marks: mods.Select(m => p.Axes.Any(a => a.Axis == axis && a.Modifier == m && ProfileSummary.HasAction(a))).ToArray());
        Editor.Children.Add(seg);
        if (_modifier != ButtonModifier.None)
            Editor.Children.Add(Text($"Only active while the {(_modifier == ButtonModifier.Both ? "left and right button are" : _modifier + " button is")} held. Without this combination the 'Always' setting applies.", muted: true, top: 8));

        var binding = p.Axes.FirstOrDefault(a => a.Axis == axis && a.Modifier == _modifier);
        if (binding == null)
        {
            var empty = new Border { Style = (Style)FindResource("Inset"), Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(16) };
            var sp = new StackPanel();
            sp.Children.Add(Text("Nothing assigned for this combination.", bold: true));
            var add = new Button { Content = "Assign an action", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) =>
            {
                p.Axes.Add(new AxisBinding { Axis = axis, Modifier = _modifier, Behavior = AxisBehavior.Continuous, Action = new ActionDef("none") });
                Edited(); BuildEditor();
            };
            sp.Children.Add(add);
            empty.Child = sp;
            Editor.Children.Add(empty);
            return;
        }

        // --- behaviour
        Editor.Children.Add(Section("Behavior"));
        Editor.Children.Add(Segmented(new[] { "Smooth movement", "One action per push" }, binding.Behavior == AxisBehavior.Continuous ? 0 : 1, i =>
        {
            var b = i == 0 ? AxisBehavior.Continuous : AxisBehavior.Discrete;
            if (b == binding.Behavior) return;
            binding.Behavior = b;
            // keep actions that still fit, drop the ones that do not
            if (b == AxisBehavior.Continuous) { binding.Action = FirstOrNone(binding.Action, continuous: true); }
            else { binding.Positive = FirstOrNone(binding.Positive, false); binding.Negative = FirstOrNone(binding.Negative, false); }
            Edited(); BuildEditor();
        }));
        Editor.Children.Add(Text(binding.Behavior == AxisBehavior.Continuous
            ? "The further you push, the faster it goes - cursor, scrolling, moving windows."
            : "One push triggers the action once - tabs, windows, shortcuts. Return to centre to trigger again.", muted: true, top: 8));

        // --- action(s)
        Editor.Children.Add(Section("Action"));
        if (binding.Behavior == AxisBehavior.Continuous)
        {
            var pick = new ActionPicker(PickerMode.Continuous) { Context = _profile, Value = binding.Action };
            pick.ValueChanged += () => { binding.Action = pick.Value; Edited(); };
            Editor.Children.Add(pick);
        }
        else
        {
            Editor.Children.Add(Text($"+  {AxisInfo.Positive(axis)}", muted: true, bottom: 4));
            var pos = new ActionPicker(PickerMode.Discrete) { Context = _profile, Value = binding.Positive };
            pos.ValueChanged += () => { binding.Positive = pos.Value; Edited(); };
            Editor.Children.Add(pos);
            Editor.Children.Add(Text($"−  {AxisInfo.Negative(axis)}", muted: true, top: 12, bottom: 4));
            var neg = new ActionPicker(PickerMode.Discrete) { Context = _profile, Value = binding.Negative };
            neg.ValueChanged += () => { binding.Negative = neg.Value; Edited(); };
            Editor.Children.Add(neg);
        }

        // --- live preview + tuning
        Editor.Children.Add(Section("Live preview"));
        Editor.Children.Add(BuildPreview(binding));

        var tuneBtn = new Button { Content = _tuningOpen ? "Hide fine tuning" : "Fine tuning...", Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        tuneBtn.Click += (_, _) => { _tuningOpen = !_tuningOpen; BuildEditor(); };
        Editor.Children.Add(tuneBtn);
        if (_tuningOpen) BuildTuning(binding);

        var rm = new Button { Content = _modifier == ButtonModifier.None ? "Clear this axis" : "Remove this combination", Style = (Style)FindResource("DangerButton"), Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        rm.Click += (_, _) => { p.Axes.Remove(binding); Edited(); BuildEditor(); };
        Editor.Children.Add(rm);
    }

    private static ActionDef? FirstOrNone(ActionDef? a, bool continuous)
    {
        if (a == null || a.Type == "none") return new ActionDef("none");
        var d = AppController.Instance.Registry.Find(a.Type);
        if (d == null) return new ActionDef("none");
        var fits = continuous ? d.CanBeContinuous : (d.Kinds & (Core.Actions.ActionKinds.Discrete | Core.Actions.ActionKinds.Hold)) != 0;
        return fits ? a : new ActionDef("none");
    }

    // ---------------------------------------------------------------- tuning
    private void BuildTuning(AxisBinding b)
    {
        var t = b.Tuning;
        var cont = b.Behavior == AxisBehavior.Continuous;
        var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        void Changed() { Edited(); _redraw?.Invoke(); }

        host.Children.Add(SwitchRow("Reverse direction", "Swap + and −.", t.Invert, v => { t.Invert = v; Changed(); }));
        host.Children.Add(SliderRow("Sensitivity", "How strongly the cap reacts.", 0.2, 3, t.Sensitivity, "0.00x", v => { t.Sensitivity = v; Changed(); }));
        host.Children.Add(SliderRow("Dead zone", "Ignore tiny movements around the centre.", 0, 0.5, t.Deadzone, "0%", v => { t.Deadzone = v; Changed(); }, percent: true));
        if (cont)
        {
            host.Children.Add(SliderRow("Speed", "Overall speed of the action.", 0.2, 3, t.Speed, "0.00x", v => { t.Speed = v; Changed(); }));
            host.Children.Add(SliderRow("Acceleration curve", "Higher = precise for small pushes, fast for big ones.", 0.5, 3, t.Acceleration, "0.00", v => { t.Acceleration = v; Changed(); }));
            host.Children.Add(SliderRow("Gentle-movement zone", "Pushes below this level are damped for fine control.", 0, 0.6, t.WeakZone, "0%", v => { t.WeakZone = v; Changed(); }, percent: true));
            host.Children.Add(SliderRow("Gentle-movement damping", "1 = no damping.", 0.05, 1, t.WeakDamping, "0.00", v => { t.WeakDamping = v; Changed(); }));
            host.Children.Add(SliderRow("Strong-push level", "From this deflection on, the boost applies.", 0.5, 1, t.StrongThreshold, "0%", v => { t.StrongThreshold = v; Changed(); }, percent: true));
            host.Children.Add(SliderRow("Strong-push boost", "Multiplier for hard pushes (1 = off).", 1, 4, t.StrongMultiplier, "0.00x", v => { t.StrongMultiplier = v; Changed(); }));
        }
        else
        {
            host.Children.Add(SliderRow("Trigger point", "How far to push before it fires.", 0.2, 0.95, t.Threshold, "0%", v => { t.Threshold = v; Changed(); }, percent: true));
            host.Children.Add(SliderRow("Pause between triggers", "Prevents accidental double triggers.", 100, 1500, t.CooldownMs, "0 ms", v => { t.CooldownMs = (int)v; Changed(); }));
            host.Children.Add(SwitchRow("Repeat while held", "Keep firing while the cap stays pushed.", t.Repeat, v => { t.Repeat = v; Changed(); BuildEditor(); }));
            if (t.Repeat) host.Children.Add(SliderRow("Repeat interval", "", 200, 1500, t.RepeatMs, "0 ms", v => { t.RepeatMs = (int)v; Changed(); }));
        }
        Editor.Children.Add(host);
    }

    // ---------------------------------------------------------------- preview graph
    private Action? _redraw;

    private UIElement BuildPreview(AxisBinding b)
    {
        const double W = 396, H = 110;
        var canvas = new Canvas { Width = W, Height = H, ClipToBounds = true };
        var frame = new Border { Style = (Style)FindResource("Inset"), Padding = new Thickness(0), Child = canvas, Height = H, Width = W, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var curve = new Polyline { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        curve.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
        var mid = new Line { X1 = 0, X2 = W, Y1 = H / 2, Y2 = H / 2, StrokeThickness = 1 };
        mid.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        var cen = new Line { X1 = W / 2, X2 = W / 2, Y1 = 0, Y2 = H, StrokeThickness = 1 };
        cen.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        var thrA = new Line { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
        var thrB = new Line { StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
        thrA.SetResourceReference(Shape.StrokeProperty, "MutedBrush"); thrB.SetResourceReference(Shape.StrokeProperty, "MutedBrush");
        var dot = new Ellipse { Width = 12, Height = 12 };
        dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
        var info = new TextBlock { FontSize = 11, Margin = new Thickness(10, 6, 0, 0) };
        info.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        canvas.Children.Add(mid); canvas.Children.Add(cen); canvas.Children.Add(thrA); canvas.Children.Add(thrB);
        canvas.Children.Add(curve); canvas.Children.Add(dot); canvas.Children.Add(info);

        double YMax() => b.Behavior == AxisBehavior.Continuous
            ? Math.Max(1.0, Enumerable.Range(0, 41).Select(i => Math.Abs(AxisMath.Continuous(b.Tuning, i / 40.0))).Max())
            : 1.0;
        double Out(double x) => b.Behavior == AxisBehavior.Continuous ? AxisMath.Continuous(b.Tuning, x) / YMax() : AxisMath.Discrete(b.Tuning, x);
        double Px(double x) => W / 2 + x * (W / 2 - 8);
        double Py(double y) => H / 2 - y * (H / 2 - 12);

        _redraw = () =>
        {
            var pts = new PointCollection();
            for (var i = -40; i <= 40; i++) { var x = i / 40.0; pts.Add(new Point(Px(x), Py(Out(x)))); }
            curve.Points = pts;
            var th = b.Behavior == AxisBehavior.Discrete ? b.Tuning.Threshold : -1;
            thrA.Visibility = thrB.Visibility = th >= 0 ? Visibility.Visible : Visibility.Collapsed;
            if (th >= 0)
            {
                // trigger point seen in input space: where does the level reach the threshold?
                double Find()
                {
                    for (var i = 0; i <= 100; i++) if (Math.Abs(AxisMath.Discrete(b.Tuning, i / 100.0)) >= th) return i / 100.0;
                    return 1.0;
                }
                var x = Find();
                thrA.X1 = thrA.X2 = Px(x); thrB.X1 = thrB.X2 = Px(-x);
                thrA.Y1 = thrB.Y1 = 0; thrA.Y2 = thrB.Y2 = H;
            }
            info.Text = b.Behavior == AxisBehavior.Continuous ? "Input → speed" : "Input → trigger (dashed = trigger point)";
        };
        _redraw();

        _previewUpdate = raw =>
        {
            var x = Math.Clamp(raw, -1, 1);
            Canvas.SetLeft(dot, Px(x) - 6); Canvas.SetTop(dot, Py(Out(x)) - 6);
        };
        _previewUpdate(0);
        return frame;
    }

    // ================================================================ button editor
    private void BuildButtonEditor(ButtonId id)
    {
        var p = _profile!;
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        head.Children.Add(new TextBlock { Text = id == ButtonId.Left ? "Left button" : "Right button", Style = (Style)FindResource("H2") });
        Editor.Children.Add(head);

        var b = p.Buttons.FirstOrDefault(x => x.Button == id);
        if (b == null)
        {
            b = new ButtonBinding { Button = id, Mode = ButtonMode.Tap, Action = new ActionDef("none") };
            p.Buttons.Add(b);
        }

        Editor.Children.Add(Section("How it triggers"));
        var modes = new[] { ButtonMode.Tap, ButtonMode.Press, ButtonMode.Hold };
        Editor.Children.Add(Segmented(new[] { "On click", "On press", "While held" }, Array.IndexOf(modes, b.Mode), i =>
        {
            b.Mode = modes[i];
            if (b.Action != null) b.Action = FilterButton(b.Action, b.Mode);
            if (b.Mode != ButtonMode.Tap) b.LongPress = null;
            Edited(); BuildEditor();
        }));
        Editor.Children.Add(Text(b.Mode switch
        {
            ButtonMode.Tap => "Fires when you release a short press. Best when the button is also used for combinations - it will not fire if you moved the cap while holding it.",
            ButtonMode.Press => "Fires the moment you press.",
            _ => "Starts when you press and ends when you release - for dragging, holding keys, grabbing windows or the radial menu.",
        }, muted: true, top: 8));

        Editor.Children.Add(Section("Action"));
        var pick = new ActionPicker(b.Mode == ButtonMode.Hold ? PickerMode.Hold : PickerMode.Discrete) { Context = _profile, Value = b.Action };
        pick.ValueChanged += () => { b.Action = pick.Value; Edited(); };
        Editor.Children.Add(pick);

        if (b.Mode == ButtonMode.Tap)
        {
            Editor.Children.Add(Section("Long press"));
            Editor.Children.Add(SwitchRow("Different action on long press", "Hold the button longer to do something else.", b.LongPress != null, v =>
            {
                b.LongPress = v ? new ActionDef("none") : null; Edited(); BuildEditor();
            }));
            if (b.LongPress != null)
            {
                var lp = new ActionPicker(PickerMode.Discrete) { Context = _profile, Value = b.LongPress, Margin = new Thickness(0, 8, 0, 0) };
                lp.ValueChanged += () => { b.LongPress = lp.Value; Edited(); };
                Editor.Children.Add(lp);
                Editor.Children.Add(SliderRow("Long-press time", "", 250, 1500, b.LongPressMs, "0 ms", v => { b.LongPressMs = (int)v; Edited(); }));
            }
        }

        var used = p.UsesButtonAsModifier(id);
        if (used)
            Editor.Children.Add(Text("This button is also a modifier for some axes (see Combinations). While you hold it and move the cap, its own click is skipped.", muted: true, top: 18));

        var clear = new Button { Content = "Clear this button", Style = (Style)FindResource("DangerButton"), Margin = new Thickness(0, 22, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) => { p.Buttons.Remove(b); Edited(); BuildEditor(); };
        Editor.Children.Add(clear);
    }

    private static ActionDef FilterButton(ActionDef a, ButtonMode m)
    {
        var d = AppController.Instance.Registry.Find(a.Type);
        if (d == null) return new ActionDef("none");
        var ok = m == ButtonMode.Hold ? (d.Kinds & Core.Actions.ActionKinds.Hold) != 0 : (d.Kinds & Core.Actions.ActionKinds.Discrete) != 0;
        return ok || a.Type == "none" ? a : new ActionDef("none");
    }

    // ================================================================ small UI helpers
    private TextBlock Text(string t, bool muted = false, bool bold = false, double top = 0, double bottom = 0)
    {
        var tb = new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, bottom) };
        if (muted) tb.Style = (Style)FindResource("Muted");
        if (bold) tb.FontWeight = FontWeights.SemiBold;
        return tb;
    }

    private UIElement Section(string t)
    {
        var tb = new TextBlock { Text = t.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 22, 0, 8) };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        return tb;
    }

    private Border Badge(string t)
    {
        var b = new Border { Width = 34, Height = 28, CornerRadius = new CornerRadius(8), Child = new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        b.SetResourceReference(Border.BackgroundProperty, "AccentSoftBrush");
        ((TextBlock)b.Child).SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        return b;
    }

    private UIElement Segmented(string[] names, int selected, Action<int> onSelect, bool[]? marks = null)
    {
        var host = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(3) };
        host.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        var grid = new UniformGrid { Columns = names.Length };
        var group = Guid.NewGuid().ToString();
        for (var i = 0; i < names.Length; i++)
        {
            var idx = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new TextBlock { Text = names[i], FontSize = 12.5 });
            if (marks != null && marks[i] && i != selected)
            {
                var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(6, 1, 0, 0) };
                dot.SetResourceReference(Shape.FillProperty, "AccentBrush");
                content.Children.Add(dot);
            }
            var rb = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = group, IsChecked = i == selected, Content = content, Padding = new Thickness(6, 7, 6, 7) };
            rb.Checked += (_, _) => { if (idx != selected) onSelect(idx); };
            grid.Children.Add(rb);
        }
        host.Child = grid;
        return host;
    }

    private UIElement SwitchRow(string title, string desc, bool value, Action<bool> onChange)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 });
        if (desc.Length > 0) sp.Children.Add(Text(desc, muted: true));
        var cb = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        cb.Click += (_, _) => onChange(cb.IsChecked == true);
        Grid.SetColumn(cb, 1);
        g.Children.Add(sp); g.Children.Add(cb);
        return g;
    }

    private UIElement SliderRow(string title, string desc, double min, double max, double value, string format, Action<double> onChange, bool percent = false)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 6) };
        var top = new Grid();
        top.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13 });
        var val = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        val.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        top.Children.Add(val);
        sp.Children.Add(top);
        if (desc.Length > 0) sp.Children.Add(Text(desc, muted: true));
        var s = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Margin = new Thickness(0, 4, 0, 0) };
        string Fmt(double v) => percent ? $"{v * 100:0}%" : format.EndsWith("x") ? $"{v:0.00}x" : format.EndsWith("ms") ? $"{v:0} ms" : $"{v:0.00}";
        val.Text = Fmt(s.Value);
        s.ValueChanged += (_, e) => { val.Text = Fmt(e.NewValue); onChange(e.NewValue); };
        sp.Children.Add(s);
        return sp;
    }
}
