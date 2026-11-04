using SpaceMouse.App.Services;
using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Profiles;
using ShapesPath = System.Windows.Shapes.Path;

namespace SpaceMouse.App.Controls;

public enum PickerMode
{
    /// <summary>One-shot action (axis direction, button tap/press).</summary>
    Discrete,
    /// <summary>Signed continuous action (cursor, scroll...).</summary>
    Continuous,
    /// <summary>Action with begin/end (button held).</summary>
    Hold,
}

/// <summary>
/// Visual action chooser: current action on top, expands to search + category chips + action tiles + parameter editors.
/// Built in code so it can be reused everywhere (axes, buttons, quick menu items, macro steps).
/// </summary>
public sealed class ActionPicker : UserControl
{
    private readonly AppController _app = AppController.Instance;
    private readonly Border _header = new();
    private readonly TextBlock _icon = new(), _title = new(), _subtitle = new(), _chevron = new();
    private readonly StackPanel _panel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _search = new();
    private readonly WrapPanel _chips = new();
    private readonly WrapPanel _tiles = new();
    private readonly StackPanel _params = new();
    private string _category = "All";
    private bool _building;
    private bool _recording;

    public ActionPicker(PickerMode mode, bool allowMacroSteps = true)
    {
        Mode = mode;
        _allowMacro = allowMacroSteps;
        BuildHeader();
        BuildPanel();
        var root = new StackPanel();
        root.Children.Add(_header);
        root.Children.Add(_panel);
        Content = root;
        Refresh();
    }

    private readonly bool _allowMacro;
    public PickerMode Mode { get; }
    private ActionDef? _value;
    /// <summary>Profile used to offer its quick menus for "menu" parameters.</summary>
    public Profile? Context { get; set; }
    public event Action? ValueChanged;

    public ActionDef? Value
    {
        get => _value;
        set { _value = value; Refresh(); }
    }

    public bool IsExpanded
    {
        get => _panel.Visibility == Visibility.Visible;
        set
        {
            _panel.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            _chevron.Text = value ? "" : "";
            if (value) { RebuildTiles(); RebuildParams(); }
        }
    }

    // ------------------------------------------------------------------ header
    private void BuildHeader()
    {
        _icon.Style = (Style)Application.Current.Resources["Icon"];
        _icon.FontSize = 18; _icon.Width = 28;
        _title.FontWeight = FontWeights.SemiBold; _title.FontSize = 13; _title.TextTrimming = TextTrimming.CharacterEllipsis;
        _subtitle.Style = (Style)Application.Current.Resources["Muted"]; _subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _chevron.Style = (Style)Application.Current.Resources["Icon"]; _chevron.FontSize = 11; _chevron.Text = "";
        _chevron.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) };
        text.Children.Add(_title); text.Children.Add(_subtitle);
        Grid.SetColumn(text, 1); Grid.SetColumn(_chevron, 2);
        grid.Children.Add(_icon); grid.Children.Add(text); grid.Children.Add(_chevron);

        _header.Child = grid;
        _header.Padding = new Thickness(12, 10, 14, 10);
        _header.CornerRadius = new CornerRadius(10);
        _header.BorderThickness = new Thickness(1);
        _header.Cursor = Cursors.Hand;
        _header.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        _header.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _header.MouseLeftButtonUp += (_, _) => IsExpanded = !IsExpanded;
    }

    private void Refresh()
    {
        var d = _value == null ? null : _app.Registry.Find(_value.Type);
        _icon.Text = IconMap.Glyph(d?.Icon ?? "block");
        _icon.SetResourceReference(TextBlock.ForegroundProperty, d == null || _value?.Type == "none" ? "MutedBrush" : "AccentBrush");
        _title.Text = d == null || _value?.Type == "none" ? "Nothing" : d.Name;
        var sum = _app.Registry.Describe(_value);
        _subtitle.Text = d == null || _value?.Type == "none" ? "Click to choose an action" : (sum == d.Name ? d.Category : sum);
        if (IsExpanded) { RebuildTiles(); RebuildParams(); }
    }

    // ------------------------------------------------------------------ panel
    private void BuildPanel()
    {
        _search.Margin = new Thickness(0, 0, 0, 8);
        _search.Tag = "Search actions...";
        _search.TextChanged += (_, _) => { if (!_building) RebuildTiles(); };
        _search.ToolTip = "Type to filter actions";

        var box = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12), BorderThickness = new Thickness(1) };
        box.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        box.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        var st = new StackPanel();
        var searchRow = new Grid();
        searchRow.Children.Add(_search);
        st.Children.Add(searchRow);
        st.Children.Add(_chips);
        st.Children.Add(_tiles);
        st.Children.Add(_params);
        box.Child = st;
        _panel.Children.Add(box);
    }

    private IEnumerable<ActionDescriptor> Allowed()
    {
        var all = _app.Registry.All.Where(a => a.Type != "none" && a.Type != "macro.wait" && (_allowMacro || a.Type != "macro.sequence"));
        return Mode switch
        {
            PickerMode.Continuous => all.Where(a => a.CanBeContinuous),
            PickerMode.Hold => all.Where(a => (a.Kinds & ActionKinds.Hold) != 0),
            _ => all.Where(a => (a.Kinds & ActionKinds.Discrete) != 0 || (a.Kinds & ActionKinds.Hold) != 0),
        };
    }

    private void RebuildTiles()
    {
        _building = true;
        var items = Allowed().ToList();
        var cats = new List<string> { "All" };
        cats.AddRange(items.Select(i => i.Category).Distinct());
        if (!cats.Contains(_category)) _category = "All";

        _chips.Children.Clear();
        foreach (var c in cats)
        {
            var chip = new System.Windows.Controls.Primitives.ToggleButton
                { Content = c, IsChecked = c == _category, Style = (Style)Application.Current.Resources["Chip"] };
            chip.Click += (_, _) => { _category = c; RebuildTiles(); };
            _chips.Children.Add(chip);
        }

        var q = _search.Text.Trim();
        var filtered = items.Where(i => (_category == "All" || i.Category == _category) &&
                                        (q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Category.Contains(q, StringComparison.OrdinalIgnoreCase)));
        _tiles.Children.Clear();
        _tiles.Children.Add(MakeTile(null));
        foreach (var d in filtered) _tiles.Children.Add(MakeTile(d));
        _building = false;
    }

    private UIElement MakeTile(ActionDescriptor? d)
    {
        var selected = d == null ? (_value == null || _value.Type == "none") : _value?.Type == d.Type;
        var ic = new TextBlock { Style = (Style)Application.Current.Resources["Icon"], Text = IconMap.Glyph(d?.Icon ?? "block"), FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
        ic.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentBrush" : "MutedBrush");
        var tx = new TextBlock { Text = d?.Name ?? "Nothing", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(ic); row.Children.Add(tx);
        var b = new Border
        {
            Child = row, Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = d?.Description ?? "Do nothing",
        };
        b.SetResourceReference(Border.BackgroundProperty, selected ? "AccentSoftBrush" : "SurfaceAltBrush");
        b.SetResourceReference(Border.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
        b.MouseLeftButtonUp += (_, _) => Choose(d);
        return b;
    }

    private void Choose(ActionDescriptor? d)
    {
        if (d == null) _value = new ActionDef("none");
        else
        {
            var def = new ActionDef(d.Type);
            foreach (var p in d.ParamList) if (!string.IsNullOrEmpty(p.Default)) def.Params[p.Key] = p.Default;
            if (p_IsMenu(d) && Context?.QuickMenus.FirstOrDefault() is { } m) def.Params["menu"] = m.Id;
            if (d.Type == "macro.sequence") def.Steps.Add(new ActionDef("keys.send", ("keys", "Ctrl+C")));
            _value = def;
        }
        Refresh();
        ValueChanged?.Invoke();
    }

    private static bool p_IsMenu(ActionDescriptor d) => d.ParamList.Any(p => p.Kind == ParamKind.Menu);

    // ------------------------------------------------------------------ parameters
    private void RebuildParams()
    {
        _params.Children.Clear();
        if (_value == null || _value.Type == "none") return;
        var d = _app.Registry.Find(_value.Type);
        if (d == null) return;

        if (d.ParamList.Length > 0 || d.Type == "macro.sequence")
        {
            var sep = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 10) };
            sep.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
            _params.Children.Add(sep);
        }

        foreach (var p in d.ParamList)
        {
            var label = new TextBlock { Text = p.Label, Style = (Style)Application.Current.Resources["Muted"], Margin = new Thickness(0, 0, 0, 4) };
            _params.Children.Add(label);
            _params.Children.Add(MakeEditor(p));
            if (p.Hint.Length > 0 && p.Kind != ParamKind.Keys)
                _params.Children.Add(new TextBlock { Text = p.Hint, Style = (Style)Application.Current.Resources["Muted"], FontSize = 11, Margin = new Thickness(0, 3, 0, 0) });
        }

        if (d.Type == "macro.sequence") _params.Children.Add(BuildMacroEditor());
    }

    private FrameworkElement MakeEditor(ParamDef p)
    {
        var current = _value!.Get(p.Key, p.Default);
        switch (p.Kind)
        {
            case ParamKind.Profile:
            case ParamKind.Menu:
            case ParamKind.Choice:
            {
                var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
                var options = p.Kind == ParamKind.Profile
                    ? _app.Profiles.Select(x => (x.Id.ToString(), x.Name)).ToList()
                    : p.Kind == ParamKind.Menu
                        ? (Context?.QuickMenus ?? new List<QuickMenu>()).Select(x => (x.Id, x.Name)).ToList()
                        : (p.Choices ?? Array.Empty<string>()).Select(x => (x, x)).ToList();
                combo.ItemsSource = options;
                combo.DisplayMemberPath = "Item2";
                combo.SelectedItem = options.FirstOrDefault(o => o.Item1 == current);
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is ValueTuple<string, string> o) { _value!.Params[p.Key] = o.Item1; ValueChanged?.Invoke(); Subtitle(); }
                };
                return combo;
            }
            case ParamKind.Keys:
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tb = new TextBox { Text = current };
                var rec = new Button { Content = "Record", Margin = new Thickness(8, 0, 0, 0), Style = (Style)Application.Current.Resources["PrimaryButton"], ToolTip = "Click, then press the key combination" };
                Grid.SetColumn(rec, 1);
                var hint = new TextBlock { Text = p.Hint, Style = (Style)Application.Current.Resources["Muted"], FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
                tb.TextChanged += (_, _) =>
                {
                    _value!.Params[p.Key] = tb.Text; ValueChanged?.Invoke(); Subtitle();
                    var ok = KeyCombo.ParseSequence(tb.Text) != null;
                    hint.Text = ok ? p.Hint : "Not a valid combination";
                    hint.SetResourceReference(TextBlock.ForegroundProperty, ok ? "MutedBrush" : "DangerBrush");
                };
                rec.Click += (_, _) => { _recording = true; rec.Content = "Press your shortcut..."; tb.Focus(); };
                tb.PreviewKeyDown += (_, e) =>
                {
                    if (!_recording) return;
                    var key = e.Key == Key.System ? e.SystemKey : e.Key;
                    var name = KeyName(key);
                    e.Handled = true;
                    if (name == null) return; // modifier alone: wait for the main key
                    var parts = new List<string>();
                    var m = Keyboard.Modifiers;
                    if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                    if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                    if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                    if (m.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
                    parts.Add(name);
                    tb.Text = string.Join("+", parts);
                    _recording = false; rec.Content = "Record";
                };
                tb.LostKeyboardFocus += (_, _) => { _recording = false; rec.Content = "Record"; };
                var wrap = new StackPanel();
                grid.Children.Add(tb); grid.Children.Add(rec);
                var caps = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                void Caps()
                {
                    caps.Children.Clear();
                    var seq = tb.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    for (var si = 0; si < seq.Length; si++)
                    {
                        if (si > 0) caps.Children.Add(new TextBlock { Text = "then", Style = (Style)Application.Current.Resources["Muted"], Margin = new Thickness(4, 6, 8, 0) });
                        var keys = seq[si].Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                        for (var ki = 0; ki < keys.Length; ki++)
                        {
                            if (ki > 0) caps.Children.Add(new TextBlock { Text = "+", Style = (Style)Application.Current.Resources["Muted"], Margin = new Thickness(5, 6, 5, 0) });
                            var cap = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1, 1, 1, 2.5), Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 0, 2),
                                Child = new TextBlock { Text = keys[ki], FontWeight = FontWeights.SemiBold, FontSize = 12.5 } };
                            cap.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
                            cap.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                            caps.Children.Add(cap);
                        }
                    }
                }
                tb.TextChanged += (_, _) => Caps();
                Caps();
                wrap.Children.Add(caps);
                wrap.Children.Add(grid);
                var common = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                common.Children.Add(new TextBlock { Text = "Common:", Style = (Style)Application.Current.Resources["Muted"], Margin = new Thickness(0, 5, 8, 0) });
                foreach (var (name, combo) in new[] { ("Copy", "Ctrl+C"), ("Paste", "Ctrl+V"), ("Undo", "Ctrl+Z"), ("Save", "Ctrl+S"), ("Close tab", "Ctrl+W"), ("New tab", "Ctrl+T"),
                    ("Switch app", "Alt+Tab"), ("Show desktop", "Win+D"), ("Task manager", "Ctrl+Shift+Esc"), ("Screenshot", "Win+Shift+S"), ("Emoji", "Win+Period"), ("Clipboard history", "Win+V") })
                {
                    var c = combo;
                    var chipBtn = new Button { Content = name, FontSize = 11.5, Padding = new Thickness(9, 4, 9, 4), Margin = new Thickness(0, 0, 5, 5), ToolTip = combo };
                    chipBtn.Click += (_, _) => tb.Text = c;
                    common.Children.Add(chipBtn);
                }
                wrap.Children.Add(common);
                wrap.Children.Add(hint);
                wrap.Margin = new Thickness(0, 0, 0, 6);
                grid.Margin = new Thickness(0);
                return wrap;
            }
            case ParamKind.Path:
            case ParamKind.Process:
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var tb = new TextBox { Text = current };
                tb.TextChanged += (_, _) => { _value!.Params[p.Key] = tb.Text; ValueChanged?.Invoke(); Subtitle(); };
                var br = new Button { Content = "Browse...", Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(br, 1);
                br.Click += (_, _) =>
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.*|All files|*.*", CheckFileExists = true };
                    if (dlg.ShowDialog() == true) tb.Text = dlg.FileName;
                };
                grid.Children.Add(tb); grid.Children.Add(br);
                return grid;
            }
            default:
            {
                var tb = new TextBox { Text = current, Margin = new Thickness(0, 0, 0, 10) };
                tb.TextChanged += (_, _) => { _value!.Params[p.Key] = tb.Text; ValueChanged?.Invoke(); Subtitle(); };
                return tb;
            }
        }
    }

    private void Subtitle()
    {
        var d = _value == null ? null : _app.Registry.Find(_value.Type);
        if (d == null) return;
        var sum = _app.Registry.Describe(_value);
        _subtitle.Text = sum == d.Name ? d.Category : sum;
    }

    private UIElement BuildMacroEditor()
    {
        var root = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        void Rebuild()
        {
            root.Children.Clear();
            root.Children.Add(new TextBlock { Text = "Steps (run one after another)", Style = (Style)Application.Current.Resources["Muted"], Margin = new Thickness(0, 0, 0, 6) });
            for (var i = 0; i < _value!.Steps.Count; i++)
            {
                var idx = i; var step = _value.Steps[i];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = (i + 1).ToString(), Style = (Style)Application.Current.Resources["Muted"], VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 0, 0) });
                FrameworkElement body;
                if (step.Type == "macro.wait")
                {
                    var tb = new TextBox { Text = step.Get("ms", "300") };
                    tb.TextChanged += (_, _) => { step.Params["ms"] = tb.Text; ValueChanged?.Invoke(); };
                    var l = new StackPanel { Orientation = Orientation.Horizontal };
                    l.Children.Add(new TextBlock { Text = "Wait (ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                    tb.Width = 90; l.Children.Add(tb);
                    body = l;
                }
                else
                {
                    var sub = new ActionPicker(PickerMode.Discrete, allowMacroSteps: false) { Context = Context, Value = step };
                    sub.ValueChanged += () => { _value.Steps[idx] = sub.Value ?? new ActionDef("none"); ValueChanged?.Invoke(); };
                    body = sub;
                }
                Grid.SetColumn(body, 1);
                var del = new Button { Content = "", Style = (Style)Application.Current.Resources["IconButton"], ToolTip = "Remove step", VerticalAlignment = VerticalAlignment.Top };
                del.Click += (_, _) => { _value.Steps.RemoveAt(idx); ValueChanged?.Invoke(); Rebuild(); };
                Grid.SetColumn(del, 2);
                row.Children.Add(body); row.Children.Add(del);
                root.Children.Add(row);
            }
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            var add = new Button { Content = "+ Action" }; var wait = new Button { Content = "+ Wait", Margin = new Thickness(8, 0, 0, 0) };
            add.Click += (_, _) => { _value!.Steps.Add(new ActionDef("keys.send", ("keys", "Ctrl+C"))); ValueChanged?.Invoke(); Rebuild(); };
            wait.Click += (_, _) => { _value!.Steps.Add(new ActionDef("macro.wait", ("ms", "300"))); ValueChanged?.Invoke(); Rebuild(); };
            btns.Children.Add(add); btns.Children.Add(wait);
            root.Children.Add(btns);
        }
        Rebuild();
        return root;
    }

    /// <summary>Maps WPF keys to the names understood by <see cref="KeyCombo"/>; null for pure modifiers/unknown keys.</summary>
    public static string? KeyName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return k.ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((int)(k - Key.D0)).ToString();
        if (k >= Key.NumPad0 && k <= Key.NumPad9) return ((int)(k - Key.NumPad0)).ToString();
        if (k >= Key.F1 && k <= Key.F24) return k.ToString();
        return k switch
        {
            Key.Left => "Left", Key.Right => "Right", Key.Up => "Up", Key.Down => "Down",
            Key.Return => "Enter", Key.Escape => "Esc", Key.Space => "Space", Key.Tab => "Tab", Key.Back => "Backspace",
            Key.Delete => "Delete", Key.Insert => "Insert", Key.Home => "Home", Key.End => "End",
            Key.PageUp => "PageUp", Key.Next => "PageDown", Key.OemPlus => "Plus", Key.OemMinus => "Minus",
            Key.OemComma => "Comma", Key.OemPeriod => "Period", Key.Snapshot => "PrintScreen",
            _ => null,
        };
    }
}
