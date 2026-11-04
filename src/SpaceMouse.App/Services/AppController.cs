using System.Windows.Threading;
using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Engine;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Persistence;
using SpaceMouse.Core.Platform;
using SpaceMouse.Core.Profiles;
using SpaceMouse.Core.Simulator;
using SpaceMouse.Windows;
using HwProfiles = SpaceMouse.Core.Hid.HardwareProfiles;

namespace SpaceMouse.App.Services;

public sealed record ActivityItem(DateTime Time, string Text, bool IsError)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

/// <summary>
/// Single composition root + application state used by all views. All events are raised on the UI thread.
/// Views never talk to the engine from other threads directly - they go through this class.
/// </summary>
public sealed class AppController : ObservableObject, IDisposable
{
    public static AppController Instance { get; private set; } = null!;

    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _fgTimer;
    private bool _profilesDirty, _hardwareDirty;
    private IRawReportSource? _hardware;
    private bool _useVirtual;
    private HardwareProfile _activeHardware;
    private readonly TimerResolution _timer = new();
    private readonly HotkeyManager _hotkey = new();

    public AppController(string dataDir)
    {
        Instance = this;
        _ui = Application.Current.Dispatcher;
        Store = new ConfigStore(dataDir);
        Settings = Store.LoadSettings();
        Registry = BuiltInActions.Create();

        Profiles = new ObservableCollection<Profile>(Store.LoadProfiles());
        foreach (var p in Profiles)
            foreach (var issue in ProfileValidator.Validate(p, Registry))
                Log($"Profile '{p.Name}': {issue.Message}", true);
        HardwareProfiles = new ObservableCollection<HardwareProfile>(Store.LoadHardwareProfiles());
        _activeHardware = HardwareProfiles.FirstOrDefault(h => h.Id == Settings.ActiveHardwareProfileId) ?? HardwareProfiles[0];

        Live = new WindowsPlatform();
        Dry = new DryRunPlatform();
        Platform = new SwitchablePlatform(Live, Dry);
        Live.Simulator.Failed += m => _ui.BeginInvoke(() => Log(m, true));

        Virtual = new VirtualSpaceMouse();
        Hub = new InputHub(_activeHardware);
        Engine = new SpaceMouseEngine(Registry, Platform) { Profiles = Profiles.ToList() };
        Hub.FrameReceived += Engine.SubmitFrame;
        Hub.SourceChanged += () => _ui.BeginInvoke(OnSourceChanged);
        Runner = new EngineRunner(Engine);

        Engine.ActionExecuted += s => _ui.BeginInvoke(() => Log(s, false));
        Engine.ErrorOccurred += s => _ui.BeginInvoke(() => { Log(s, true); Toast(s, true); });
        Engine.NotificationRaised += s => _ui.BeginInvoke(() => Toast(s, false));
        Engine.ModeChanged += _ => _ui.BeginInvoke(() => { Raise(nameof(IsActive)); Raise(nameof(StatusText)); StateChanged?.Invoke(); });
        Engine.ProfileChanged += p => _ui.BeginInvoke(() => { Raise(nameof(ActiveProfile)); StateChanged?.Invoke(); });
        Platform.DryRun.Logged += s => { if (_useVirtual && !Platform.Live) _ui.BeginInvoke(() => Log("(simulated) " + s, false)); };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); FlushSave(); };
        _fgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _fgTimer.Tick += (_, _) => PollForeground();
    }

    // ---------------- services ----------------
    public ConfigStore Store { get; }
    public AppSettings Settings { get; }
    public ActionRegistry Registry { get; }
    public ObservableCollection<Profile> Profiles { get; }
    public ObservableCollection<HardwareProfile> HardwareProfiles { get; }
    public WindowsPlatform Live { get; }
    public DryRunPlatform Dry { get; }
    public SwitchablePlatform Platform { get; }
    public VirtualSpaceMouse Virtual { get; }
    public InputHub Hub { get; }
    public SpaceMouseEngine Engine { get; }
    public EngineRunner Runner { get; }
    public ObservableCollection<ActivityItem> Activity { get; } = new();

    public event Action? StateChanged;
    /// <summary>Raised when the main window should be shown (tray, second instance).</summary>
    public event Action? ShowMainWindowRequested;
    public event Action<string, bool>? ToastRequested;

    // ---------------- lifecycle ----------------
    public void Start()
    {
        ApplyInputMethod();
        Runner.Start();

        var active = Profiles.FirstOrDefault(p => p.Id == Settings.ActiveProfileId) ?? Profiles.First();
        Engine.SetProfile(active);
        Engine.AutoSwitchEnabled = Settings.AutoSwitchProfiles;
        if (Settings.ActivateOnStart || false) Engine.SetMode(EngineMode.Custom);

        _hotkey.Pressed += () => _ui.BeginInvoke(() => { Engine.ToggleActive(); Toast(IsActive ? "SpaceMouse control ON" : "SpaceMouse control OFF", false); });
        ApplyHotkey();
        _fgTimer.Start();
        foreach (var w in Store.Warnings) { Log(w, true); }
        OnSourceChanged();
    }

    public void Dispose()
    {
        try { Engine.SetMode(EngineMode.Standard); } catch { }
        try { Engine.ReleaseAllInput(); } catch { }
        try { FlushSave(); } catch { }
        Runner.Dispose();
        Hub.Dispose();
        _hotkey.Dispose();
        Engine.Dispose();
        _timer.Dispose();
    }

    public void RequestShowMainWindow() => ShowMainWindowRequested?.Invoke();

    /// <summary>Ask the main window to open a page: Dashboard, Customize, QuickMenu, Profiles, Device, Settings.</summary>
    public event Action<string>? NavigateRequested;
    public void Navigate(string page) { RequestShowMainWindow(); NavigateRequested?.Invoke(page); }

    // ---------------- input source ----------------
    public bool UseVirtual => _useVirtual;
    public SourceKind ActiveSource => Hub.ActiveKind;
    public HardwareProfile ActiveHardware => _activeHardware;

    public string SourceTitle => ActiveSource switch
    {
        SourceKind.Hardware => Hub.Device?.Name ?? "SpaceMouse",
        SourceKind.Virtual => "Virtual controller",
        _ => _useVirtual ? "Virtual controller (disconnected)" : "No device connected",
    };

    public string SourceBadge => ActiveSource switch
    {
        SourceKind.Hardware => "Real hardware",
        SourceKind.Virtual => "Simulation",
        _ => "No device",
    };

    public string SourceDetail
    {
        get
        {
            if (ActiveSource == SourceKind.Hardware && Hub.Device is { } d)
                return $"USB {d.UsbId}" + (string.IsNullOrEmpty(d.Manufacturer) ? "" : $" · {d.Manufacturer}");
            if (ActiveSource == SourceKind.Virtual) return "Software simulation - no hardware involved";
            return _useVirtual ? "The virtual device was unplugged" : "Plug in your SpaceMouse or open the virtual controller";
        }
    }

    public bool SendsToSystem => !(_useVirtual && !Settings.VirtualControllerSendsToSystem);
    public bool NeedsCalibration => ActiveSource == SourceKind.Hardware && !_activeHardware.Calibrated;

    /// <summary>Report ids the device sends that the active hardware profile cannot decode.</summary>
    public bool HasUnrecognisedReports =>
        ActiveSource == SourceKind.Hardware && Hub.SeenReports.Any(r => !_activeHardware.Reports.Any(l => l.ReportId == r.Id));

    public long ReportCount => (_hardware as RawInputHidSource)?.ReportCount ?? (_hardware as DirectHidSource)?.ReportCount ?? 0;
    public string? SourceError => (_hardware as RawInputHidSource)?.LastError ?? (_hardware as DirectHidSource)?.LastError;

    public void ApplyInputMethod()
    {
        var wasVirtual = _useVirtual;
        Hub.Attach(null);
        _hardware?.Dispose();
        _hardware = Settings.InputMethod == "DirectHid" ? new DirectHidSource() : new RawInputHidSource();
        if (wasVirtual) AttachVirtual(); else AttachHardware();
    }

    private void AttachHardware()
    {
        _useVirtual = false;
        Hub.SetProfile(_activeHardware);
        Hub.Attach(_hardware);
        Platform.Live = true;
    }

    private void AttachVirtual()
    {
        _useVirtual = true;
        Hub.SetProfile(Virtual.MatchingProfile());
        Hub.Attach(Virtual);
        Platform.Live = Settings.VirtualControllerSendsToSystem;
    }

    public void UseVirtualController(bool on)
    {
        if (on == _useVirtual) return;
        Engine.ReleaseAllInput();
        if (on) AttachVirtual(); else AttachHardware();
        OnSourceChanged();
    }

    public void ConfigureVirtual(VirtualLayout layout, double range)
    {
        Virtual.Configure(layout, range);
        if (_useVirtual) Hub.SetProfile(Virtual.MatchingProfile());
    }

    public void SetVirtualSendsToSystem(bool on)
    {
        Engine.ReleaseAllInput();
        Settings.VirtualControllerSendsToSystem = on;
        if (_useVirtual) Platform.Live = on;
        SaveSettings(); OnSourceChanged();
    }

    private void OnSourceChanged()
    {
        Raise(nameof(ActiveSource)); Raise(nameof(SourceTitle)); Raise(nameof(SourceBadge)); Raise(nameof(SourceDetail));
        Raise(nameof(UseVirtual)); Raise(nameof(SendsToSystem)); Raise(nameof(NeedsCalibration)); Raise(nameof(HasUnrecognisedReports));
        Raise(nameof(StatusText));
        StateChanged?.Invoke();
    }

    /// <summary>Called periodically by pages that show live diagnostics.</summary>
    public void RefreshDiagnostics() { Raise(nameof(ReportCount)); Raise(nameof(HasUnrecognisedReports)); Raise(nameof(SourceError)); }

    // ---------------- hardware profiles ----------------
    public void ApplyHardwareProfile(HardwareProfile hp)
    {
        _activeHardware = hp;
        Settings.ActiveHardwareProfileId = hp.Id;
        if (!_useVirtual) Hub.SetProfile(hp);
        SaveSettings(); SaveHardware();
        OnSourceChanged();
    }

    public void HardwareProfileEdited(HardwareProfile hp)
    {
        if (hp.Id == _activeHardware.Id && !_useVirtual) Hub.SetProfile(hp);
        _hardwareDirty = true; _saveTimer.Stop(); _saveTimer.Start();
        Raise(nameof(NeedsCalibration));
    }

    public void AddHardwareProfile(HardwareProfile hp) { HardwareProfiles.Add(hp); SaveHardware(); }

    public void RemoveHardwareProfile(HardwareProfile hp)
    {
        if (HardwareProfiles.Count <= 1) return;
        HardwareProfiles.Remove(hp);
        if (hp.Id == _activeHardware.Id) ApplyHardwareProfile(HardwareProfiles[0]);
        SaveHardware();
    }

    /// <summary>Replaces the active profile's report layouts with a guess made from the reports actually received.</summary>
    public bool ApplyGuessedLayout()
    {
        var guess = HwProfiles.Guess(Hub.SeenReports);
        if (guess == null) return false;
        _activeHardware.Reports = guess.Reports;
        HardwareProfileEdited(_activeHardware);
        return true;
    }

    // ---------------- mode & profiles ----------------
    public bool IsActive => Engine.Mode == EngineMode.Custom;
    public Profile? ActiveProfile => Engine.ActiveProfile;

    public string StatusText => IsActive
        ? (ActiveSource == SourceKind.None ? "Active - waiting for a device" : $"Active · {ActiveProfile?.Name}")
        : "Standard mode - SpaceMouse not controlled by this app";

    public void SetActive(bool on)
    {
        Engine.SetMode(on ? EngineMode.Custom : EngineMode.Standard);
        Settings.LastModeCustom = on; SaveSettings();
        Raise(nameof(IsActive));
    }

    public void ActivateProfile(Profile p)
    {
        Engine.SetProfile(p);
        Settings.ActiveProfileId = p.Id; SaveSettings();
    }

    /// <summary>Call after any edit of a profile; saves (debounced) and reloads the engine if it is the active one.</summary>
    public void ProfileEdited(Profile p)
    {
        _profilesDirty = true; _saveTimer.Stop(); _saveTimer.Start();
        if (Engine.ActiveProfile?.Id == p.Id) Engine.Reload();
        StateChanged?.Invoke();
    }

    private void SyncEngineProfiles() { Engine.Profiles = Profiles.ToList(); }

    public Profile CreateProfile(string name = "New profile", Profile? basedOn = null)
    {
        var p = basedOn?.Clone() ?? Presets.Create(Presets.Custom);
        p.Id = Guid.NewGuid(); p.PresetKey = null; p.Name = name;
        foreach (var m in p.QuickMenus) m.Id = Guid.NewGuid().ToString("N")[..8];
        Profiles.Add(p); SyncEngineProfiles(); ProfileEdited(p);
        return p;
    }

    public Profile DuplicateProfile(Profile src)
    {
        var p = CreateProfile(src.Name + " (copy)", src);
        // quick menu ids changed in the copy: fix references inside actions
        for (var i = 0; i < src.QuickMenus.Count; i++)
        {
            var oldId = src.QuickMenus[i].Id; var newId = p.QuickMenus[i].Id;
            void Fix(ActionDef? a) { if (a != null && a.Params.TryGetValue("menu", out var m) && m == oldId) a.Params["menu"] = newId; }
            foreach (var b in p.Buttons) { Fix(b.Action); Fix(b.LongPress); }
            foreach (var ax in p.Axes) { Fix(ax.Action); Fix(ax.Positive); Fix(ax.Negative); }
        }
        return p;
    }

    public void DeleteProfile(Profile p)
    {
        if (Profiles.Count <= 1) return;
        var wasActive = Engine.ActiveProfile?.Id == p.Id;
        Profiles.Remove(p); SyncEngineProfiles();
        if (wasActive) ActivateProfile(Profiles[0]);
        ProfileEdited(Profiles[0]);
    }

    public void ResetPreset(Profile p)
    {
        if (p.PresetKey == null) return;
        var fresh = Presets.Create(p.PresetKey);
        var i = Profiles.IndexOf(p);
        fresh.AppRules = p.AppRules; // keep the user's app assignments
        Profiles[i] = fresh; SyncEngineProfiles();
        if (Engine.ActiveProfile?.Id == p.Id) Engine.SetProfile(fresh);
        ProfileEdited(fresh);
    }

    public void ImportProfiles(string path)
    {
        foreach (var p in ConfigStore.ImportProfiles(path, Registry)) { Profiles.Add(p); }
        SyncEngineProfiles(); ProfileEdited(Profiles[^1]);
    }

    public void ExportProfile(Profile p, string path) => ConfigStore.ExportProfile(p, path);

    public void ProfilesChanged() { SyncEngineProfiles(); ProfileEdited(ActiveProfile ?? Profiles[0]); }

    // ---------------- settings ----------------
    public void SaveSettings() { try { Store.SaveSettings(Settings); } catch (Exception e) { Log("Could not save settings: " + e.Message, true); } }

    public void ApplySettings()
    {
        Engine.AutoSwitchEnabled = Settings.AutoSwitchProfiles;
        if (!Settings.AutoSwitchProfiles) Engine.ApplyForeground(null);
        ApplyHotkey();
        try
        {
            var exe = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exe) && AutoStart.IsEnabled() != Settings.StartWithWindows) AutoStart.Set(Settings.StartWithWindows, exe);
        }
        catch (Exception e) { Log("Autostart setting failed: " + e.Message, true); }
        SaveSettings();
        ThemeService.Apply(Settings.Theme);
    }

    public string? HotkeyError { get; private set; }

    private void ApplyHotkey()
    {
        _hotkey.Register(Settings.ToggleHotkey);
        HotkeyError = _hotkey.LastError;
        Raise(nameof(HotkeyError));
    }

    private void FlushSave()
    {
        try
        {
            if (_profilesDirty) { Store.SaveProfiles(Profiles); _profilesDirty = false; }
            if (_hardwareDirty) { Store.SaveHardwareProfiles(HardwareProfiles); _hardwareDirty = false; }
        }
        catch (Exception e) { Log("Could not save: " + e.Message, true); }
    }

    private void SaveHardware() { _hardwareDirty = true; FlushSave(); }

    // ---------------- misc ----------------
    private void PollForeground()
    {
        if (!Settings.AutoSwitchProfiles || !IsActive) return;
        try
        {
            var name = Live.System.ForegroundProcessName();
            if (string.Equals(name, "SpaceMouseStudio", StringComparison.OrdinalIgnoreCase)) return;
            Engine.ApplyForeground(name);
        }
        catch { /* best effort */ }
    }

    public void Log(string text, bool isError)
    {
        Activity.Insert(0, new ActivityItem(DateTime.Now, text, isError));
        while (Activity.Count > 40) Activity.RemoveAt(Activity.Count - 1);
    }

    public void Toast(string text, bool isError) { if (Settings.ShowToasts || isError) ToastRequested?.Invoke(text, isError); }
}
