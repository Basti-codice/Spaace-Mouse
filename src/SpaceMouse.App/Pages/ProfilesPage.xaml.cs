using System.Diagnostics;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.App.Pages;

/// <summary>Profile cards (activate, duplicate, rename, export, reset, delete) + optional per-app auto switching.</summary>
public partial class ProfilesPage : UserControl, IPage
{
    private static readonly string[] IconChoices =
        { "star", "desktop", "web", "bolt", "window", "code", "music", "mail", "folder", "settings", "calendar", "note", "calc", "camera", "heart", "play" };

    private readonly AppController _app = AppController.Instance;
    private Profile? _editing;        // card in rename mode
    private Profile? _ruleProfile;    // profile whose app rules are shown
    private bool _syncing;

    public ProfilesPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { _app.StateChanged += OnState; OnShown(); };
        Unloaded += (_, _) => _app.StateChanged -= OnState;
    }

    public void OnShown()
    {
        _syncing = true; AutoSwitch.IsChecked = _app.Settings.AutoSwitchProfiles; _syncing = false;
        if (_ruleProfile == null || !_app.Profiles.Contains(_ruleProfile)) _ruleProfile = _app.ActiveProfile ?? _app.Profiles.FirstOrDefault();
        Rebuild();
    }

    private void OnState()
    {
        if (_editing != null) return; // do not rebuild while the user is typing
        try { Rebuild(); } catch { }
    }

    private void Rebuild()
    {
        BuildCards();
        BuildRules();
    }

    // -------------------------------------------------------------- cards
    private void BuildCards()
    {
        CardsHost.Children.Clear();
        foreach (var p in _app.Profiles.ToList()) CardsHost.Children.Add(BuildCard(p));
    }

    private Border BuildCard(Profile p)
    {
        var active = _app.ActiveProfile?.Id == p.Id;
        var card = new Border { Style = (Style)FindResource("Card"), Width = 330, Margin = new Thickness(0, 0, 16, 16), Padding = new Thickness(18) };
        if (active)
        {
            card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            card.BorderThickness = new Thickness(1.5);
        }
        var stack = new StackPanel();
        card.Child = stack;

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var iconBox = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top };
        iconBox.SetResourceReference(Border.BackgroundProperty, active ? "AccentBrush" : "SurfaceAltBrush");
        var glyph = new TextBlock { Style = (Style)FindResource("Icon"), Text = IconMap.Glyph(p.Icon), FontSize = 20 };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, active ? "OnAccentBrush" : "AccentBrush");
        iconBox.Child = glyph;
        head.Children.Add(iconBox);

        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        stack.Children.Add(head);

        if (_editing == p) { BuildEditMode(p, info, stack, glyph); return card; }

        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 });
        if (p.PresetKey != null) nameRow.Children.Add(Badge("PRESET", "SurfaceAltBrush", "MutedBrush"));
        if (active) nameRow.Children.Add(Badge("ACTIVE", "AccentSoftBrush", "AccentBrush"));
        info.Children.Add(nameRow);
        info.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(p.Description) ? "No description" : p.Description,
            Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), MinHeight = 34, MaxHeight = 52,
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var act = new Button { Content = active ? "In use" : "Activate", IsEnabled = !active, Style = active ? null : (Style)FindResource("PrimaryButton"), Padding = new Thickness(14, 7, 14, 7) };
        act.Click += (_, _) => Safe(() => { _app.ActivateProfile(p); });
        actions.Children.Add(act);
        actions.Children.Add(IconBtn("", "Duplicate", () => { var c = _app.DuplicateProfile(p); _ruleProfile = c; }));
        actions.Children.Add(IconBtn("", "Edit name, description and icon", () => { _editing = p; Rebuild(); }));
        actions.Children.Add(IconBtn("", "Export to file...", () => Export(p)));
        actions.Children.Add(IconBtn("", "Auto-switch apps for this profile", () => { _ruleProfile = p; BuildRules(); }));
        if (p.PresetKey != null) actions.Children.Add(IconBtn("", "Reset to default", () => Reset(p)));
        var del = IconBtn("", "Delete", () => Delete(p));
        del.IsEnabled = _app.Profiles.Count > 1;
        del.SetResourceReference(Button.ForegroundProperty, "DangerBrush");
        actions.Children.Add(del);
        stack.Children.Add(actions);
        return card;
    }

    private void BuildEditMode(Profile p, StackPanel info, StackPanel stack, TextBlock glyph)
    {
        var name = new TextBox { Text = p.Name, FontWeight = FontWeights.SemiBold };
        var desc = new TextBox { Text = p.Description, Margin = new Thickness(0, 6, 0, 0), AcceptsReturn = false };
        info.Children.Add(name); info.Children.Add(desc);
        var icon = p.Icon;

        var picker = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var boxes = new Dictionary<string, Border>();
        void Paint(Border b, bool sel)
        {
            b.SetResourceReference(Border.BackgroundProperty, sel ? "AccentSoftBrush" : "SurfaceAltBrush");
            b.SetResourceReference(Border.BorderBrushProperty, sel ? "AccentBrush" : "BorderBrush");
        }
        foreach (var k in IconChoices)
        {
            var key = k;
            var b = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 4, 4), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = key,
                Child = new TextBlock { Style = (Style)FindResource("Icon"), Text = IconMap.Glyph(key), FontSize = 15 } };
            Paint(b, key == icon);
            b.MouseLeftButtonUp += (_, _) => { icon = key; glyph.Text = IconMap.Glyph(key); foreach (var (kk, bb) in boxes) Paint(bb, kk == key); };
            boxes[key] = b; picker.Children.Add(b);
        }
        stack.Children.Add(picker);

        void Save()
        {
            if (!string.IsNullOrWhiteSpace(name.Text)) p.Name = name.Text.Trim();
            p.Description = desc.Text.Trim(); p.Icon = icon;
            _editing = null;
            _app.ProfileEdited(p);
            Rebuild();
        }
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); else if (e.Key == Key.Escape) { _editing = null; Rebuild(); } };
        desc.KeyDown += (_, e) => { if (e.Key == Key.Enter) Save(); else if (e.Key == Key.Escape) { _editing = null; Rebuild(); } };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Save", Style = (Style)FindResource("PrimaryButton") };
        ok.Click += (_, _) => Safe(Save);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { _editing = null; Rebuild(); };
        row.Children.Add(ok); row.Children.Add(cancel);
        stack.Children.Add(row);
        Dispatcher.BeginInvoke(new Action(() => { name.Focus(); name.SelectAll(); }));
    }

    private Border Badge(string text, string bg, string fg)
    {
        var t = new TextBlock { Text = text, FontSize = 9.5, FontWeight = FontWeights.SemiBold };
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var b = new Border { Child = t, CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    private Button IconBtn(string glyph, string tip, Action run)
    {
        var b = new Button { Content = glyph, Style = (Style)FindResource("IconButton"), ToolTip = tip, Margin = new Thickness(4, 0, 0, 0) };
        b.Click += (_, _) => Safe(run);
        return b;
    }

    private void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { MessageBox.Show("That did not work: " + e.Message, "Space Mouse Studio", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    // -------------------------------------------------------------- actions
    private void OnNew(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var p = _app.CreateProfile("New profile");
        _editing = p; _ruleProfile = p;
        Rebuild();
    });

    private void OnImport(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Space Mouse Studio profile (*.json)|*.json|All files|*.*", Title = "Import profile" };
        if (dlg.ShowDialog() != true) return;
        try { _app.ImportProfiles(dlg.FileName); _app.Toast("Profile imported.", false); }
        catch (Exception ex)
        {
            MessageBox.Show("This file is not a valid profile.\n\n" + ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Rebuild();
    });

    private void Export(Profile p)
    {
        var safe = string.Concat(p.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Space Mouse Studio profile (*.json)|*.json", FileName = safe + ".json", Title = "Export profile" };
        if (dlg.ShowDialog() != true) return;
        _app.ExportProfile(p, dlg.FileName);
        _app.Toast("Profile exported.", false);
    }

    private void Delete(Profile p)
    {
        if (_app.Profiles.Count <= 1) return;
        if (MessageBox.Show($"Delete the profile '{p.Name}'? This cannot be undone.", "Delete profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (_ruleProfile == p) _ruleProfile = null;
        _app.DeleteProfile(p);
        _ruleProfile ??= _app.ActiveProfile ?? _app.Profiles.FirstOrDefault();
        Rebuild();
    }

    private void Reset(Profile p)
    {
        if (MessageBox.Show($"Reset '{p.Name}' to its original settings? Your changes to this preset are lost (app assignments are kept).", "Reset preset", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var wasRules = _ruleProfile == p;
        _app.ResetPreset(p);
        if (wasRules) _ruleProfile = _app.Profiles.FirstOrDefault(x => x.PresetKey == p.PresetKey);
        Rebuild();
    }

    private void OnAutoSwitch(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _app.Settings.AutoSwitchProfiles = AutoSwitch.IsChecked == true;
        try { _app.ApplySettings(); } catch { }
    }

    // -------------------------------------------------------------- app rules
    private void BuildRules()
    {
        RulesHost.Children.Clear();
        var p = _ruleProfile;
        if (p == null) return;

        RulesHost.Children.Add(new TextBlock { Text = $"Programs for '{p.Name}'", Style = (Style)FindResource("H3") });
        if (p.AppRules.Count == 0)
            RulesHost.Children.Add(new TextBlock { Text = "No programs assigned. Add one below, or use the gear button on a profile card.", Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 6, 0, 0) });

        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var rule in p.AppRules.ToList())
        {
            var r = rule;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = r.ProcessName, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
            var x = new Button { Content = "", Style = (Style)FindResource("IconButton"), Width = 24, Height = 24, FontSize = 10, Margin = new Thickness(6, 0, -4, 0), ToolTip = "Remove" };
            x.Click += (_, _) => { p.AppRules.Remove(r); _app.ProfileEdited(p); BuildRules(); };
            row.Children.Add(x);
            var chip = new Border { Child = row, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 4, 8, 4), Margin = new Thickness(0, 0, 8, 8), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
            chip.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            chips.Children.Add(chip);
        }
        RulesHost.Children.Add(chips);

        void Add(string name)
        {
            name = (name ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length == 0 || p.AppRules.Any(r => string.Equals(r.ProcessName, name, StringComparison.OrdinalIgnoreCase))) return;
            p.AppRules.Add(new AppRule { ProcessName = name });
            _app.ProfileEdited(p);
            BuildRules();
        }

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tb = new TextBox { ToolTip = "Process name, e.g. chrome or code" };
        var add = new Button { Content = "Add", Margin = new Thickness(8, 0, 20, 0) };
        add.Click += (_, _) => { Add(tb.Text); };
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Add(tb.Text); };
        var combo = new ComboBox();
        combo.DropDownOpened += (_, _) => FillRunning(combo);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) Add(s); };
        Grid.SetColumn(add, 1); Grid.SetColumn(combo, 2);
        grid.Children.Add(tb); grid.Children.Add(add); grid.Children.Add(combo);
        RulesHost.Children.Add(grid);
        RulesHost.Children.Add(new TextBlock { Text = "Type a process name on the left, or pick a running app on the right.", Style = (Style)FindResource("Muted"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
    }

    private static void FillRunning(ComboBox combo)
    {
        try
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in Process.GetProcesses())
            {
                try { if (pr.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(pr.ProcessName)) names.Add(pr.ProcessName); }
                catch { }
                finally { pr.Dispose(); }
            }
            combo.ItemsSource = names.ToList();
        }
        catch { combo.ItemsSource = new List<string>(); }
    }
}
