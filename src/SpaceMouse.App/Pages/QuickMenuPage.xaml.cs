using SpaceMouse.App.Controls;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Profiles;
using MenuItem = SpaceMouse.Core.Profiles.MenuItem;

namespace SpaceMouse.App.Pages;

/// <summary>Editor for the radial quick menus of the active profile.</summary>
public partial class QuickMenuPage : UserControl, IPage
{
    private sealed record AxisChoice(Axis Axis, string Name);

    private readonly AppController _app = AppController.Instance;
    private Profile? _profile;
    private QuickMenu? _menu;
    private readonly List<MenuItem> _path = new();
    private MenuItem? _sel;
    private bool _building;
    private bool _renaming;

    private static readonly List<AxisChoice> AxisChoices =
        AxisInfo.All.Select(a => new AxisChoice(a, $"{AxisInfo.Name(a)} ({AxisInfo.Short(a)})")).ToList();

    public QuickMenuPage()
    {
        InitializeComponent();
        Preview.ItemClicked += i =>
        {
            var cur = Cur;
            if (cur != null && i >= 0 && i < cur.Count) Select(cur[i]);
        };
        Loaded += (_, _) => { _app.StateChanged += OnState; OnShown(); };
        Unloaded += (_, _) => _app.StateChanged -= OnState;
    }

    private List<MenuItem>? Cur => _menu == null ? null : _path.Count == 0 ? _menu.Items : _path[^1].Children;

    public void OnShown() => Load();

    private void OnState()
    {
        try
        {
            if (!ReferenceEquals(_app.ActiveProfile, _profile)) Load();
            else UpdateTryHint();
        }
        catch { /* UI refresh must never throw */ }
    }

    private void Edited()
    {
        if (_profile == null) return;
        try { _app.ProfileEdited(_profile); } catch { }
    }

    // ------------------------------------------------------------ loading
    private void Load()
    {
        var prevMenuId = _menu?.Id;
        var sameProfile = ReferenceEquals(_app.ActiveProfile, _profile);
        _profile = _app.ActiveProfile;
        _path.Clear(); _sel = null;
        if (_profile == null || _profile.QuickMenus.Count == 0)
        {
            _menu = null;
            EmptyState.Visibility = Visibility.Visible; MainGrid.Visibility = Visibility.Collapsed;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed; MainGrid.Visibility = Visibility.Visible;
        _menu = (sameProfile ? _profile.QuickMenus.FirstOrDefault(m => m.Id == prevMenuId) : null) ?? _profile.QuickMenus[0];
        RebuildMenuList();
        RebuildAll();
    }

    private void RebuildMenuList()
    {
        _building = true;
        MenuList.ItemsSource = null;
        MenuList.ItemsSource = _profile!.QuickMenus;
        MenuList.SelectedItem = _menu;
        _building = false;
    }

    private void RebuildAll()
    {
        RebuildPreview();
        RebuildItems();
        RebuildEditor();
        RebuildSettings();
        UpdateTryHint();
    }

    private void UpdateTryHint()
    {
        TryHint.Text = _app.IsActive
            ? "Opens at your cursor. Point with the SpaceMouse; left button confirms, right button cancels."
            : "Turn on SpaceMouse control (switch at the top right) to try the real menu.";
    }

    // ------------------------------------------------------------ menu list actions
    private void OnMenuSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_building || MenuList.SelectedItem is not QuickMenu m) return;
        _menu = m; _path.Clear(); _sel = null;
        RebuildAll();
    }

    private void OnAddMenu(object sender, RoutedEventArgs e)
    {
        _profile = _app.ActiveProfile;
        if (_profile == null) return;
        var m = new QuickMenu { Name = $"Menu {_profile.QuickMenus.Count + 1}" };
        m.Items.Add(new MenuItem { Label = "Screenshot", Icon = "screenshot", Action = new ActionDef("system.screenshot") });
        m.Items.Add(new MenuItem { Label = "Task view", Icon = "taskview", Action = new ActionDef("system.taskview") });
        m.Items.Add(new MenuItem { Label = "Show desktop", Icon = "desktop", Action = new ActionDef("system.showdesktop") });
        _profile.QuickMenus.Add(m);
        _menu = m;
        Edited();
        Load();
    }

    private void OnDuplicateMenu(object sender, RoutedEventArgs e)
    {
        if (_profile == null || _menu == null) return;
        var c = _menu.Clone();
        c.Id = Guid.NewGuid().ToString("N")[..8]; c.Name = _menu.Name + " (copy)";
        _profile.QuickMenus.Add(c); _menu = c;
        Edited(); Load();
    }

    private void OnDeleteMenu(object sender, RoutedEventArgs e)
    {
        if (_profile == null || _menu == null) return;
        if (MessageBox.Show($"Delete the menu '{_menu.Name}'?", "Delete menu", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _profile.QuickMenus.Remove(_menu);
        _menu = null;
        Edited(); Load();
    }

    private void OnRenameMenu(object sender, RoutedEventArgs e)
    {
        if (_menu == null) return;
        _renaming = true;
        RenameBox.Text = _menu.Name; RenameBox.Visibility = Visibility.Visible;
        RenameBox.Focus(); RenameBox.SelectAll();
    }

    private void CommitRename()
    {
        if (!_renaming) return;
        _renaming = false;
        RenameBox.Visibility = Visibility.Collapsed;
        if (_menu != null && !string.IsNullOrWhiteSpace(RenameBox.Text) && RenameBox.Text.Trim() != _menu.Name)
        {
            _menu.Name = RenameBox.Text.Trim();
            Edited();
            RebuildMenuList();
            RebuildPreview();
        }
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CommitRename();
        else if (e.Key == Key.Escape) { _renaming = false; RenameBox.Visibility = Visibility.Collapsed; }
    }

    private void OnRenameLost(object sender, RoutedEventArgs e) => CommitRename();

    private void OnTry(object sender, RoutedEventArgs e)
    {
        if (_menu == null) return;
        if (!_app.IsActive) { _app.Toast("Turn on SpaceMouse control first to try the menu.", true); return; }
        _app.Engine.OpenQuickMenu(_menu.Id, holdMode: false);
    }

    // ------------------------------------------------------------ preview
    private int SelIndex => _sel == null || Cur == null ? -1 : Cur.IndexOf(_sel);

    private void RebuildPreview()
    {
        var cur = Cur;
        if (cur == null || _menu == null) return;
        var title = _path.Count == 0 ? _menu.Name : _path[^1].Label;
        Preview.SetMenu(new List<MenuItem>(cur), SelIndex, title);
        PathText.Text = _path.Count == 0 ? _menu.Name : _menu.Name + " › " + string.Join(" › ", _path.Select(p => p.Label));
        UpButton.Visibility = _path.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnUpLevel(object sender, RoutedEventArgs e)
    {
        if (_path.Count == 0) return;
        _sel = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        RebuildAll();
    }

    // ------------------------------------------------------------ items list
    private void RebuildItems()
    {
        var cur = Cur;
        _building = true;
        ItemList.Items.Clear();
        if (cur != null)
            foreach (var it in cur) ItemList.Items.Add(ItemRow(it));
        if (_sel != null && cur != null && cur.IndexOf(_sel) is var i and >= 0) ItemList.SelectedIndex = i;
        _building = false;
        var n = cur?.Count ?? 0;
        ItemsHint.Text = n == 0 ? "This menu is empty. Add your first item with +."
            : n > 12 ? "More than 12 items get crowded - consider a submenu."
            : $"{n} item{(n == 1 ? "" : "s")}. They are placed clockwise, starting at the top.";
    }

    private static UIElement ItemRow(MenuItem it)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };
        row.Children.Add(new TextBlock { Style = (Style)Application.Current.Resources["Icon"], Text = IconMap.Glyph(it.Icon), Width = 26, FontSize = 15 });
        row.Children.Add(new TextBlock { Text = it.Label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        if (it.HasChildren)
        {
            var sub = new TextBlock { Text = $"  submenu · {it.Children.Count}", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            row.Children.Add(sub);
        }
        return row;
    }

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_building) return;
        var cur = Cur;
        if (cur == null) return;
        var i = ItemList.SelectedIndex;
        _sel = i >= 0 && i < cur.Count ? cur[i] : null;
        RebuildPreview();
        RebuildEditor();
    }

    private void Select(MenuItem item)
    {
        _sel = item;
        RebuildItems(); RebuildPreview(); RebuildEditor();
    }

    private void OnAddItem(object sender, RoutedEventArgs e)
    {
        var cur = Cur; if (cur == null) return;
        var it = new MenuItem { Label = "New item", Icon = "star", Action = new ActionDef("none") };
        cur.Add(it); _sel = it;
        Edited(); RebuildItems(); RebuildPreview(); RebuildEditor();
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        var cur = Cur; if (cur == null || _sel == null) return;
        var i = cur.IndexOf(_sel);
        cur.Remove(_sel);
        _sel = cur.Count == 0 ? null : cur[Math.Min(i, cur.Count - 1)];
        Edited(); RebuildItems(); RebuildPreview(); RebuildEditor();
    }

    private void Move(int dir)
    {
        var cur = Cur; if (cur == null || _sel == null) return;
        var i = cur.IndexOf(_sel); var j = i + dir;
        if (i < 0 || j < 0 || j >= cur.Count) return;
        (cur[i], cur[j]) = (cur[j], cur[i]);
        Edited(); RebuildItems(); RebuildPreview();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(+1);

    // ------------------------------------------------------------ item editor
    private void RebuildEditor()
    {
        var host = ItemEditorHost;
        host.Children.Clear();
        host.Children.Add(new TextBlock { Text = "Selected item", Style = (Style)FindResource("H3") });
        if (_sel == null)
        {
            host.Children.Add(new TextBlock { Text = "Choose an item in the list or click a segment of the preview to edit it.", Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            return;
        }
        var item = _sel;

        host.Children.Add(Label("Name"));
        var name = new TextBox { Text = item.Label };
        name.TextChanged += (_, _) =>
        {
            item.Label = name.Text; Edited();
            RebuildPreview();
            if (ItemList.SelectedIndex >= 0 && ItemList.SelectedIndex < ItemList.Items.Count)
            {
                _building = true; ItemList.Items[ItemList.SelectedIndex] = ItemRow(item); ItemList.SelectedIndex = Cur!.IndexOf(item); _building = false;
            }
        };
        host.Children.Add(name);

        host.Children.Add(Label("Icon"));
        host.Children.Add(BuildIconPicker(item));

        host.Children.Add(Label("What it does"));
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        var rbAction = new RadioButton { Content = "Run an action", Style = (Style)FindResource("Segment"), GroupName = "itemkind", IsChecked = !item.HasChildren };
        var rbSub = new RadioButton { Content = "Open a submenu", Style = (Style)FindResource("Segment"), GroupName = "itemkind", IsChecked = item.HasChildren };
        seg.Children.Add(rbAction); seg.Children.Add(rbSub);
        var segBox = new Border { Style = (Style)FindResource("Inset"), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, Child = seg };
        host.Children.Add(segBox);

        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        host.Children.Add(body);
        void BuildBody()
        {
            body.Children.Clear();
            if (item.HasChildren)
            {
                body.Children.Add(new TextBlock { Text = $"This item opens a submenu with {item.Children.Count} entr{(item.Children.Count == 1 ? "y" : "ies")}.", Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
                var b = new Button { Content = "Edit sub-items  ", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                b.Click += (_, _) => { _path.Add(item); _sel = item.Children.FirstOrDefault(); RebuildAll(); };
                body.Children.Add(b);
            }
            else
            {
                var picker = new ActionPicker(PickerMode.Discrete) { Context = _profile, Value = item.Action ?? new ActionDef("none") };
                picker.ValueChanged += () => { item.Action = picker.Value; Edited(); RebuildPreview(); };
                body.Children.Add(picker);
            }
        }
        BuildBody();

        rbAction.Checked += (_, _) =>
        {
            if (!item.HasChildren) return;
            if (MessageBox.Show("Switching to an action removes the submenu entries. Continue?", "Submenu", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            { rbSub.IsChecked = true; return; }
            item.Children.Clear(); item.Action ??= new ActionDef("none");
            Edited(); BuildBody(); RebuildItems(); RebuildPreview();
        };
        rbSub.Checked += (_, _) =>
        {
            if (item.HasChildren) return;
            item.Action = null;
            item.Children.Add(new MenuItem { Label = "Sub item", Icon = "star", Action = new ActionDef("none") });
            Edited(); BuildBody(); RebuildItems(); RebuildPreview();
        };
    }

    private TextBlock Label(string t) => new()
    {
        Text = t, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 5),
    };

    private UIElement BuildIconPicker(MenuItem item)
    {
        var wrap = new WrapPanel();
        var borders = new Dictionary<string, Border>();
        foreach (var key in IconMap.Keys)
        {
            var k = key;
            var tb = new TextBlock { Style = (Style)FindResource("Icon"), Text = IconMap.Glyph(k), FontSize = 16 };
            var b = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Child = tb, ToolTip = k, BorderThickness = new Thickness(1) };
            borders[k] = b;
            b.MouseLeftButtonUp += (_, _) =>
            {
                item.Icon = k; Edited();
                foreach (var (kk, bb) in borders) Paint(bb, kk == k);
                RebuildPreview(); RebuildItems();
            };
            Paint(b, k == item.Icon);
            wrap.Children.Add(b);
        }
        return new ScrollViewer { MaxHeight = 118, Content = wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        static void Paint(Border b, bool sel)
        {
            b.SetResourceReference(Border.BackgroundProperty, sel ? "AccentSoftBrush" : "SurfaceAltBrush");
            b.SetResourceReference(Border.BorderBrushProperty, sel ? "AccentBrush" : "BorderBrush");
        }
    }

    // ------------------------------------------------------------ menu settings
    private void RebuildSettings()
    {
        var host = SettingsHost;
        host.Children.Clear();
        var m = _menu;
        if (m == null) return;
        host.Children.Add(new TextBlock { Text = "Menu behaviour", Style = (Style)FindResource("H3") });

        host.Children.Add(Label("How do you choose an item?"));
        var rbDir = new RadioButton { Content = "Point in a direction", Style = (Style)FindResource("Segment"), GroupName = "selmode", IsChecked = m.SelectMode == MenuSelectMode.Direction };
        var rbRot = new RadioButton { Content = "Twist to step through", Style = (Style)FindResource("Segment"), GroupName = "selmode", IsChecked = m.SelectMode == MenuSelectMode.Rotation };
        var seg = new StackPanel { Orientation = Orientation.Horizontal }; seg.Children.Add(rbDir); seg.Children.Add(rbRot);
        host.Children.Add(new Border { Style = (Style)FindResource("Inset"), Padding = new Thickness(4), HorizontalAlignment = HorizontalAlignment.Left, Child = seg });

        var detail = new StackPanel();
        host.Children.Add(detail);
        void BuildDetail()
        {
            detail.Children.Clear();
            if (m.SelectMode == MenuSelectMode.Direction)
            {
                detail.Children.Add(Label("Left / right axis"));
                detail.Children.Add(AxisCombo(m.DirectionX, a => { m.DirectionX = a; Edited(); }));
                detail.Children.Add(Label("Forward / back axis"));
                detail.Children.Add(AxisCombo(m.DirectionY, a => { m.DirectionY = a; Edited(); }));
            }
            else
            {
                detail.Children.Add(Label("Stepping axis"));
                detail.Children.Add(AxisCombo(m.RotateAxis, a => { m.RotateAxis = a; Edited(); }));
                detail.Children.Add(Label($"Step speed: one item every {m.RotateStepMs} ms"));
                var s = new Slider { Minimum = 100, Maximum = 600, Value = m.RotateStepMs };
                var lbl = detail.Children[^1] as TextBlock;
                s.ValueChanged += (_, e) => { m.RotateStepMs = (int)e.NewValue; if (lbl != null) lbl.Text = $"Step speed: one item every {m.RotateStepMs} ms"; Edited(); };
                detail.Children.Add(s);
            }
            var dzLabel = Label($"Dead zone: {m.SelectDeadzone:0.00}");
            detail.Children.Add(dzLabel);
            var dz = new Slider { Minimum = 0.1, Maximum = 0.8, Value = m.SelectDeadzone };
            dz.ValueChanged += (_, e) => { m.SelectDeadzone = Math.Round(e.NewValue, 2); dzLabel.Text = $"Dead zone: {m.SelectDeadzone:0.00}"; Edited(); };
            detail.Children.Add(dz);
            detail.Children.Add(new TextBlock { Text = "How far you must move before an item is highlighted.", Style = (Style)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        }
        BuildDetail();
        rbDir.Checked += (_, _) => { m.SelectMode = MenuSelectMode.Direction; Edited(); BuildDetail(); };
        rbRot.Checked += (_, _) => { m.SelectMode = MenuSelectMode.Rotation; Edited(); BuildDetail(); };

        var push = new CheckBox { Style = (Style)FindResource("Switch"), IsChecked = m.ConfirmWithPush, VerticalAlignment = VerticalAlignment.Center };
        push.Click += (_, _) => { m.ConfirmWithPush = push.IsChecked == true; Edited(); };
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txt = new StackPanel();
        txt.Children.Add(new TextBlock { Text = "Push down to confirm", FontSize = 13 });
        txt.Children.Add(new TextBlock { Text = "Pressing the cap down selects the highlighted item.", Style = (Style)FindResource("Muted"), FontSize = 11.5 });
        Grid.SetColumn(push, 1);
        row.Children.Add(txt); row.Children.Add(push);
        host.Children.Add(row);
    }

    private ComboBox AxisCombo(Axis current, Action<Axis> set)
    {
        var cb = new ComboBox { ItemsSource = AxisChoices, DisplayMemberPath = "Name" };
        cb.SelectedItem = AxisChoices.First(a => a.Axis == current);
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is AxisChoice c) set(c.Axis); };
        return cb;
    }
}
