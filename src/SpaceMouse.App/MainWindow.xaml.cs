using System.Windows.Interop;
using System.Windows.Media.Animation;
using SpaceMouse.App.Pages;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Input;

namespace SpaceMouse.App;

public partial class MainWindow : Window
{
    private readonly AppController _app = AppController.Instance;
    private readonly Dictionary<string, (string Title, string Subtitle, string Icon, Func<UserControl> Make)> _pages;
    private readonly Dictionary<string, UserControl> _cache = new();
    private readonly Dictionary<string, RadioButton> _nav = new();
    private string _current = "";
    private string _bannerTarget = "";
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        _pages = new()
        {
            ["Dashboard"] = ("Home", "Your SpaceMouse at a glance.", "home", () => new DashboardPage()),
            ["Customize"] = ("Customize", "Click an axis or button to decide what it does.", "edit", () => new CustomizePage()),
            ["QuickMenu"] = ("Quick Menu", "A radial menu that opens around your cursor.", "radial", () => new QuickMenuPage()),
            ["Profiles"] = ("Profiles", "Presets and your own setups. Switch in one click.", "profile", () => new ProfilesPage()),
            ["Device"] = ("Device", "Connection, calibration and the virtual controller.", "cursor", () => new DevicePage()),
            ["Settings"] = ("Settings", "Appearance, startup and safety.", "settings", () => new SettingsPage()),
        };
        foreach (var (key, p) in _pages)
        {
            var rb = new RadioButton { Style = (Style)FindResource("NavItem"), GroupName = "nav", Tag = key };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Style = (Style)FindResource("Icon"), Text = IconMap.Glyph(p.Icon), FontSize = 16, Width = 26 });
            row.Children.Add(new TextBlock { Text = p.Title, FontSize = 13.5, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
            rb.Content = row;
            rb.Checked += (_, _) => Navigate(key);
            NavPanel.Children.Add(rb);
            _nav[key] = rb;
        }

        _app.StateChanged += Sync;
        _app.NavigateRequested += k => Dispatcher.BeginInvoke(new Action(() => { if (_nav.TryGetValue(k, out var rb)) rb.IsChecked = true; }));
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Closing += OnClosing;
        _nav["Dashboard"].IsChecked = true;
        Sync();
    }

    private void Navigate(string key)
    {
        if (_current == key) return;
        _current = key;
        var (title, sub, _, make) = _pages[key];
        PageTitle.Text = title; PageSubtitle.Text = sub;
        if (!_cache.TryGetValue(key, out var page)) { page = make(); _cache[key] = page; }
        PageHost.Content = page;
        (page as IPage)?.OnShown();
        page.Opacity = 0;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    private void Sync()
    {
        ActiveSwitch.IsChecked = _app.IsActive;
        StatusText.Text = _app.StatusText;
        NavSource.Text = _app.SourceBadge + " · " + _app.SourceTitle;
        NavDetail.Text = _app.SourceDetail;
        var key = _app.ActiveSource switch { SourceKind.Hardware => "SuccessBrush", SourceKind.Virtual => "AccentBrush", _ => "MutedBrush" };
        NavDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);

        // contextual banner (most important first)
        if (_app.NeedsCalibration) ShowBanner("Your SpaceMouse is connected but not calibrated yet. A one-minute calibration makes every axis behave correctly.", "Calibrate now", "Calibrate");
        else if (_app.HasUnrecognisedReports) ShowBanner("The device sends data this hardware profile does not understand. Let the app detect the layout.", "Open Device", "Device");
        else Banner.Visibility = Visibility.Collapsed;
    }

    private void ShowBanner(string text, string button, string target)
    {
        BannerText.Text = text; BannerButton.Content = button; _bannerTarget = target;
        Banner.Visibility = Visibility.Visible;
    }

    private void OnBannerClick(object sender, RoutedEventArgs e)
    {
        if (_bannerTarget == "Calibrate") { _nav["Device"].IsChecked = true; Views.CalibrationWindow.Run(this); }
        else if (_nav.TryGetValue(_bannerTarget, out var rb)) rb.IsChecked = true;
    }

    private void OnActiveClick(object sender, RoutedEventArgs e) => _app.SetActive(ActiveSwitch.IsChecked == true);

    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); Topmost = true; Topmost = false; Focus();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var app = (App)Application.Current;
        if (app.IsQuitting) return;
        if (_app.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown) { _trayHintShown = true; _app.Toast("Space Mouse Studio keeps running in the tray. Use the tray icon to quit.", false); }
        }
        else app.Quit();
    }
}
