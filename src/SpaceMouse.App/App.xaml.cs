using System.Threading;
using System.Windows.Threading;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Persistence;

namespace SpaceMouse.App;

public partial class App : Application
{
    private Mutex? _single;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;
    private TrayService? _tray;
    private Views.QuickMenuOverlayWindow? _overlay;
    private bool _quitting;
    public bool IsQuitting => _quitting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance: a second launch just brings up the first window.
        _single = new Mutex(true, "SpaceMouseStudio.SingleInstance", out var first);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SpaceMouseStudio.Show");
        if (!first)
        {
            try { _showEvent.Set(); } catch { }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => EmergencyRelease();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => EmergencyRelease();
        SessionEnding += (_, _) => Quit();

        try
        {
            ApplyWindowDefaults();
            var dir = ResolveDataDir();
            ThemeService.Initialize();
            _controller = new AppController(dir);
            ThemeService.Apply(_controller.Settings.Theme);
            _controller.ToastRequested += ToastWindow.ShowToast;
            _controller.Start();

            _overlay = new Views.QuickMenuOverlayWindow(_controller.Engine);
            _tray = new TrayService(_controller);

            var win = new MainWindow();
            MainWindow = win;
            _controller.ShowMainWindowRequested += () => Dispatcher.BeginInvoke(new Action(() => win.BringToFront()));

            var minimized = e.Args.Contains("--minimized") || _controller.Settings.StartMinimized;
            if (!minimized) win.Show();
            else if (!_controller.Settings.MinimizeToTray) { win.WindowState = WindowState.Minimized; win.Show(); }

            var t = new Thread(() =>
            {
                while (!_quitting)
                {
                    if (_showEvent!.WaitOne(500)) Dispatcher.BeginInvoke(new Action(() => win.BringToFront()));
                }
            }) { IsBackground = true, Name = "SingleInstanceListener" };
            t.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Space Mouse Studio could not start:\n\n" + ex.Message, "Space Mouse Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            Quit();
        }
    }

    /// <summary>Gives every Window (also derived ones) theme colours and font, so text inherits the right Foreground.</summary>
    private void ApplyWindowDefaults()
    {
        try
        {
            var st = new Style(typeof(Window));
            st.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new DynamicResourceExtension("TextBrush")));
            st.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new DynamicResourceExtension("BgBrush")));
            st.Setters.Add(new Setter(System.Windows.Controls.Control.FontFamilyProperty, Resources["UiFont"]));
            FrameworkElement.StyleProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(st));
        }
        catch { /* cosmetic: windows then fall back to explicit settings */ }
    }

    /// <summary>Portable mode: if a file named "portable.flag" sits next to the EXE, all data lives in .\data.</summary>
    private static string ResolveDataDir()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "portable.flag"))) return Path.Combine(exeDir, "data");
        return ConfigStore.DefaultDirectory;
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Never crash with keys held: release first, then keep running.
        EmergencyRelease();
        _controller?.Log("Unexpected error: " + e.Exception.Message, true);
        _controller?.Toast("Unexpected error: " + e.Exception.Message, true);
        e.Handled = true;
    }

    private void EmergencyRelease()
    {
        try { _controller?.Engine.SetMode(SpaceMouse.Core.Engine.EngineMode.Standard); } catch { }
        try { _controller?.Engine.ReleaseAllInput(); } catch { }
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        try { if (_controller != null) { _controller.Settings.LastModeCustom = _controller.IsActive; _controller.SaveSettings(); } } catch { }
        try { _overlay?.Close(); } catch { }
        try { _tray?.Dispose(); } catch { }
        try { _controller?.Dispose(); } catch { }
        try { _single?.ReleaseMutex(); } catch { }
        Shutdown();
    }
}
