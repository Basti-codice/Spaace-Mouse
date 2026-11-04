using System.Windows.Threading;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Input;

namespace SpaceMouse.App.Pages;

public partial class DashboardPage : UserControl, IPage
{
    private readonly AppController _app = AppController.Instance;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _syncing;

    public DashboardPage()
    {
        InitializeComponent();
        ActivityList.ItemsSource = _app.Activity;
        _app.Activity.CollectionChanged += (_, _) => NoActivity.Visibility = _app.Activity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProfileList.ItemsSource = _app.Profiles;
        Device.TargetClicked += OnTargetClicked;
        _timer.Tick += (_, _) => Device.UpdateLive(_app.Hub.Current);
        Loaded += (_, _) => { _app.StateChanged += Sync; _timer.Start(); Sync(); };
        Unloaded += (_, _) => { _app.StateChanged -= Sync; _timer.Stop(); };
    }

    public void OnShown() => Sync();

    private void Sync()
    {
        _syncing = true;
        try
        {
            ModeStandard.IsChecked = !_app.IsActive;
            ModeCustom.IsChecked = _app.IsActive;
            ModeText.Text = _app.IsActive
                ? "Custom: the active profile interprets every movement and button of the SpaceMouse."
                : "Standard: this app does nothing. The SpaceMouse behaves exactly as without it.";
            HotkeyText.Text = string.IsNullOrWhiteSpace(_app.Settings.ToggleHotkey)
                ? "" : $"Panic switch: press {_app.Settings.ToggleHotkey} anywhere to toggle control instantly.";
            ProfileList.SelectedItem = _app.Profiles.FirstOrDefault(p => p.Id == _app.ActiveProfile?.Id);

            SourceBadge.Text = _app.SourceBadge;
            SourceTitle.Text = _app.SourceTitle;
            SourceDetail.Text = _app.SourceDetail;
            var brush = _app.ActiveSource switch { SourceKind.Hardware => "SuccessBrush", SourceKind.Virtual => "AccentBrush", _ => "MutedBrush" };
            SourceDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brush);

            Device.SetSummaries(a => ProfileSummary.AxisText(_app.ActiveProfile, a), b => ProfileSummary.Button(_app.ActiveProfile, b));
            Onboarding.Visibility = _app.Settings.FirstRunDone ? Visibility.Collapsed : Visibility.Visible;
            NoActivity.Visibility = _app.Activity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _syncing = false; }
    }

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _app.SetActive(ModeCustom.IsChecked == true);
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ProfileList.SelectedItem is not SpaceMouse.Core.Profiles.Profile p) return;
        if (_app.ActiveProfile?.Id != p.Id) _app.ActivateProfile(p);
    }

    private void OnTargetClicked(string target)
    {
        CustomizePage.PendingTarget = target;
        _app.Navigate("Customize");
    }

    private void OnDismissOnboarding(object sender, RoutedEventArgs e) { _app.Settings.FirstRunDone = true; _app.SaveSettings(); Sync(); }
    private void OnCalibrate(object sender, RoutedEventArgs e) => Views.CalibrationWindow.Run(Window.GetWindow(this));
    private void OnVirtual(object sender, RoutedEventArgs e) => Views.VirtualControllerWindow.ShowSingleton();
}
