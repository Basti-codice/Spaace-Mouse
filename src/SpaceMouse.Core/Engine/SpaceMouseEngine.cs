using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Platform;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.Core.Engine;

public enum EngineMode
{
    /// <summary>Pass-through: the app injects nothing. The device behaves as without this software.</summary>
    Standard,
    /// <summary>The active profile interprets all movements and buttons.</summary>
    Custom,
}

/// <summary>
/// Input processing + mode manager + action dispatch. Works only with normalised <see cref="InputFrame"/>s,
/// so hardware and virtual controller behave identically.
/// </summary>
public sealed class SpaceMouseEngine : IEngineControl, IDisposable
{
    private readonly object _gate = new();
    private readonly ActionRegistry _registry;
    private readonly IPlatform _platform;
    private readonly ActionContext _ctx;

    private InputFrame _latest = InputFrame.Neutral;
    private bool _prevL, _prevR;
    private readonly ButtonRuntime[] _btn = { new(), new() };
    private readonly Dictionary<AxisBinding, BindingRuntime> _rt = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ButtonBinding, (ActionHandler Main, ActionHandler? Long)> _btnHandlers = new(ReferenceEqualityComparer.Instance);
    private readonly AxisBinding?[] _current = new AxisBinding?[6];
    private readonly double[] _processed = new double[6];
    private double _time;
    private Profile? _profile;
    private Profile? _baseProfile;
    private readonly Dictionary<string, double> _errorTimes = new();

    public SpaceMouseEngine(ActionRegistry registry, IPlatform platform)
    {
        _registry = registry;
        _platform = platform;
        _ctx = new ActionContext(platform, this, registry);
        Menu = new QuickMenuController();
        Menu.Closed += OnMenuClosed;
    }

    // ---------------- public surface ----------------
    public ActionRegistry Registry => _registry;
    public QuickMenuController Menu { get; }
    public List<Profile> Profiles { get; set; } = new();
    public EngineMode Mode { get; private set; } = EngineMode.Standard;
    public Profile? ActiveProfile { get { lock (_gate) return _profile; } }
    public bool AutoSwitchEnabled { get; set; }
    public double[] Processed { get { lock (_gate) return (double[])_processed.Clone(); } }

    public event Action<EngineMode>? ModeChanged;
    public event Action<Profile?>? ProfileChanged;
    /// <summary>Human readable log of executed discrete actions.</summary>
    public event Action<string>? ActionExecuted;
    public event Action<string>? ErrorOccurred;
    public event Action<string>? NotificationRaised;

    // ---------------- input ----------------
    public void SubmitFrame(InputFrame f) { lock (_gate) _latest = f; }

    /// <summary>Advance the engine by <paramref name="dt"/> seconds using the latest frame.</summary>
    public void Tick(double dt)
    {
        lock (_gate)
        {
            _time += dt;
            var f = _latest;
            if (Mode != EngineMode.Custom || _profile == null)
            {
                _prevL = f.Left; _prevR = f.Right; // keep edges in sync so enabling doesn't trigger stale presses
                Array.Clear(_processed);
                return;
            }

            ProcessButtons(f);

            if (Menu.IsOpen)
            {
                DeactivateAllAxes();
                Menu.Update(f, _time);
                Array.Clear(_processed);
                return;
            }

            foreach (var axis in AxisInfo.All)
                ProcessAxis(axis, f, dt);
        }
    }

    // ---------------- mode / profile management ----------------
    public void SetMode(EngineMode mode)
    {
        lock (_gate)
        {
            if (mode == Mode) return;
            if (mode == EngineMode.Standard) ReleaseEverything();
            else { _prevL = _latest.Left; _prevR = _latest.Right; }
            Mode = mode;
        }
        ModeChanged?.Invoke(mode);
    }

    public void ToggleActive() => SetMode(Mode == EngineMode.Custom ? EngineMode.Standard : EngineMode.Custom);

    public void SetProfile(Profile? profile, bool manual = true)
    {
        lock (_gate)
        {
            if (ReferenceEquals(profile, _profile) && !manual) return;
            ReleaseEverything();
            _profile = profile;
            if (manual) _baseProfile = profile;
            RebuildRuntime();
        }
        ProfileChanged?.Invoke(profile);
    }

    /// <summary>Re-instantiates all handlers (call after the active profile was edited).</summary>
    public void Reload()
    {
        lock (_gate)
        {
            ReleaseEverything();
            RebuildRuntime();
        }
        ProfileChanged?.Invoke(_profile);
    }

    public void NextProfile() => Cycle(+1);
    public void PrevProfile() => Cycle(-1);

    private void Cycle(int dir)
    {
        Profile? target = null;
        lock (_gate)
        {
            if (Profiles.Count == 0) return;
            var i = _profile == null ? -1 : Profiles.FindIndex(p => p.Id == _profile.Id);
            target = Profiles[((i + dir) % Profiles.Count + Profiles.Count) % Profiles.Count];
        }
        SetProfile(target);
        Notify($"Profile: {target.Name}");
    }

    public bool SwitchProfile(string idOrName)
    {
        Profile? p;
        lock (_gate)
        {
            p = Profiles.FirstOrDefault(x => x.Id.ToString() == idOrName)
                ?? Profiles.FirstOrDefault(x => string.Equals(x.Name, idOrName, StringComparison.OrdinalIgnoreCase));
        }
        if (p == null) { Report($"Profile '{idOrName}' not found."); return false; }
        SetProfile(p);
        Notify($"Profile: {p.Name}");
        return true;
    }

    /// <summary>Optional per-application auto switch. Falls back to the manually chosen profile.</summary>
    public void ApplyForeground(string? processName)
    {
        if (!AutoSwitchEnabled) return;
        Profile? target;
        lock (_gate)
        {
            var n = Normalize(processName);
            target = n == null ? null : Profiles.FirstOrDefault(p => p.AppRules.Any(r => Normalize(r.ProcessName) == n));
            target ??= _baseProfile;
            if (target == null || (_profile != null && target.Id == _profile.Id)) return;
        }
        SetProfile(target, manual: false);
    }

    private static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.ToLowerInvariant();
    }

    // ---------------- quick menu ----------------
    public void OpenQuickMenu(string? menuId, bool holdMode)
    {
        lock (_gate)
        {
            var menu = _profile?.FindMenu(menuId);
            if (menu == null) { Report("This profile has no quick menu."); return; }
            if (menu.Items.Count == 0) { Report("The quick menu is empty."); return; }
            var (x, y) = _platform.Input.GetCursor();
            Menu.Open(menu, holdMode, x, y, _time);
        }
    }

    public void ConfirmQuickMenu() { lock (_gate) Menu.Confirm(); }
    public void CancelQuickMenu() { lock (_gate) Menu.Cancel(); }

    private void OnMenuClosed(MenuItem? item)
    {
        if (item?.Action == null) return;
        // Run after the menu closed so the click/keys go to the window beneath.
        Safe(item.Label, () => _registry.Create(item.Action, _ctx).Execute(), describe: _registry.Describe(item.Action));
    }

    // ---------------- notifications ----------------
    public void Notify(string message) => NotificationRaised?.Invoke(message);

    private void Report(string message)
    {
        lock (_errorTimes)
        {
            if (_errorTimes.TryGetValue(message, out var t) && _time - t < 2.0) return;
            _errorTimes[message] = _time;
        }
        ErrorOccurred?.Invoke(message);
    }

    // ---------------- internals ----------------
    private sealed class BindingRuntime
    {
        public ActionHandler Cont = new NullAction();
        public ActionHandler Pos = new NullAction();
        public ActionHandler Neg = new NullAction();
        public bool Active;
        public bool PosArmed = true, NegArmed = true;
        public double LastFire = -100, NextRepeat;
    }

    private sealed class ButtonRuntime
    {
        public double DownAt;
        public bool Down, UsedAsModifier, Began;
    }

    private void RebuildRuntime()
    {
        _rt.Clear(); _btnHandlers.Clear();
        Array.Clear(_current);
        if (_profile == null) return;
        foreach (var a in _profile.Axes)
            _rt[a] = new BindingRuntime
            {
                Cont = a.Behavior == AxisBehavior.Continuous ? _registry.Create(a.Action, _ctx) : new NullAction(),
                Pos = a.Behavior == AxisBehavior.Discrete ? _registry.Create(a.Positive, _ctx) : new NullAction(),
                Neg = a.Behavior == AxisBehavior.Discrete ? _registry.Create(a.Negative, _ctx) : new NullAction(),
            };
        foreach (var b in _profile.Buttons)
            _btnHandlers[b] = (_registry.Create(b.Action, _ctx), b.LongPress != null ? _registry.Create(b.LongPress, _ctx) : null);
    }

    /// <summary>Releases held keys/buttons, ends holds, closes the menu. Always safe; guarantees no stuck input.</summary>
    private void ReleaseEverything()
    {
        if (Menu.IsOpen) Menu.Cancel();
        DeactivateAllAxes();
        foreach (var b in _profile?.Buttons ?? new List<ButtonBinding>())
        {
            var rt = _btn[(int)b.Button];
            if (rt.Began && _btnHandlers.TryGetValue(b, out var h))
                try { h.Main.End(); } catch { /* best effort */ }
            rt.Began = false;
        }
        foreach (var r in _btn) { r.UsedAsModifier = r.Down; r.Down = false; }   // a still-held button must not fire a Tap when released later
        _ctx.Grab.Handle = 0;
        _ctx.CancelMacros();
        try { _platform.Input.ReleaseAll(); } catch { /* never throw while releasing */ }
        Array.Clear(_processed);
    }

    /// <summary>Also release on the "other" platform when the app toggles between live and dry-run.</summary>
    public void ReleaseAllInput()
    {
        lock (_gate)
        {
            ReleaseEverything();
            if (_platform is SwitchablePlatform sp)
            {
                try { sp.DryRun.ReleaseAll(); } catch { }
            }
        }
    }

    private void DeactivateAllAxes()
    {
        for (var i = 0; i < 6; i++)
        {
            var b = _current[i];
            if (b != null && _rt.TryGetValue(b, out var r) && r.Active)
            {
                r.Active = false;
                try { r.Cont.Deactivate(); } catch { }
            }
            _current[i] = null;
        }
    }

    private void ProcessButtons(InputFrame f)
    {
        if (f.Left != _prevL) { OnButton(ButtonId.Left, f.Left); _prevL = f.Left; }
        if (f.Right != _prevR) { OnButton(ButtonId.Right, f.Right); _prevR = f.Right; }
    }

    private void OnButton(ButtonId id, bool down)
    {
        var rt = _btn[(int)id];
        var binding = _profile!.ResolveButton(id);

        if (down)
        {
            rt.Down = true; rt.DownAt = _time; rt.UsedAsModifier = false;
            if (Menu.IsOpen)
            {
                // Sticky menu: left confirms, right cancels.
                if (id == ButtonId.Left) Menu.Confirm(); else Menu.Cancel();
                rt.UsedAsModifier = true; // swallow the release too
                return;
            }
            if (binding == null || !_btnHandlers.TryGetValue(binding, out var h)) return;
            switch (binding.Mode)
            {
                case ButtonMode.Press: Safe(binding.Action, () => h.Main.Execute()); break;
                case ButtonMode.Hold: rt.Began = true; Safe(binding.Action, () => h.Main.Begin(), log: false); break;
            }
        }
        else
        {
            var held = _time - rt.DownAt;
            var used = rt.UsedAsModifier;
            rt.Down = false;
            if (binding == null || !_btnHandlers.TryGetValue(binding, out var h)) { rt.Began = false; return; }
            switch (binding.Mode)
            {
                case ButtonMode.Hold:
                    if (rt.Began) { rt.Began = false; Safe(binding.Action, () => h.Main.End(), log: false); }
                    break;
                case ButtonMode.Tap:
                    if (used) break;
                    if (binding.LongPress != null && h.Long != null && held * 1000 >= binding.LongPressMs)
                        Safe(binding.LongPress, () => h.Long.Execute());
                    else
                        Safe(binding.Action, () => h.Main.Execute());
                    break;
            }
        }
    }

    private void ProcessAxis(Axis axis, InputFrame f, double dt)
    {
        var i = (int)axis;
        var binding = _profile!.ResolveAxis(axis, f.Left, f.Right);
        var prev = _current[i];

        if (!ReferenceEquals(prev, binding))
        {
            if (prev != null && _rt.TryGetValue(prev, out var pr))
            {
                if (pr.Active) { pr.Active = false; Safe(prev.Action, () => pr.Cont.Deactivate(), log: false); }
            }
            _current[i] = binding;
            if (binding != null && _rt.TryGetValue(binding, out var nr))
            {
                // Binding switched (modifier pressed/released): wait until the axis returns to rest before re-triggering.
                nr.PosArmed = nr.NegArmed = false;
            }
        }

        _processed[i] = 0;
        if (binding == null || !_rt.TryGetValue(binding, out var rt)) return;

        var t = binding.Tuning;
        var raw = f.Get(i);

        if (binding.Behavior == AxisBehavior.Continuous)
        {
            var value = AxisMath.Continuous(t, raw);
            if (value == 0)
            {
                if (rt.Active) { rt.Active = false; Safe(binding.Action, () => rt.Cont.Deactivate(), log: false); }
                return;
            }
            _processed[i] = value;
            MarkModifierUsed(binding.Modifier);
            if (!rt.Active) { rt.Active = true; Safe(binding.Action, () => rt.Cont.Activate(), log: false); }
            Safe(binding.Action, () => rt.Cont.Tick(value, dt), log: false);
        }
        else
        {
            var level = AxisMath.Discrete(t, raw);
            var m = Math.Abs(level);
            var sign = level < 0 ? -1 : 1;
            _processed[i] = level;
            var rearm = Math.Max(0.05, t.Threshold * 0.6);
            if (m < rearm) { rt.PosArmed = rt.NegArmed = true; rt.NextRepeat = 0; return; }
            if (m < t.Threshold) return;

            var positive = sign > 0;
            ref var armed = ref positive ? ref rt.PosArmed : ref rt.NegArmed;
            var def = positive ? binding.Positive : binding.Negative;
            var handler = positive ? rt.Pos : rt.Neg;
            if (!positive) rt.PosArmed = true; else rt.NegArmed = true; // direction flip re-arms the other side

            var cooled = _time - rt.LastFire >= t.CooldownMs / 1000.0;
            var fire = false;
            if (armed && cooled) fire = true;
            else if (!armed && t.Repeat && rt.NextRepeat > 0 && _time >= rt.NextRepeat && cooled) fire = true;
            if (!fire) return;

            armed = false;
            rt.LastFire = _time;
            rt.NextRepeat = t.Repeat ? _time + t.RepeatMs / 1000.0 : 0;
            if (def == null || def.Type == "none") return;
            MarkModifierUsed(binding.Modifier);
            Safe(def, () => handler.Execute());
        }
    }

    private void MarkModifierUsed(ButtonModifier m)
    {
        if (m is ButtonModifier.Left or ButtonModifier.Both) _btn[0].UsedAsModifier = true;
        if (m is ButtonModifier.Right or ButtonModifier.Both) _btn[1].UsedAsModifier = true;
    }

    private void Safe(ActionDef? def, Action a, bool log = true)
        => Safe(def?.Type ?? "action", a, log ? _registry.Describe(def) : null);

    private void Safe(string name, Action a, string? describe)
    {
        try
        {
            a();
            if (describe != null) ActionExecuted?.Invoke(describe);
        }
        catch (Exception e)
        {
            Report($"{name}: {e.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Mode = EngineMode.Standard;
            ReleaseEverything();
            _ctx.Cancel.Cancel();
        }
    }
}
