using SpaceMouse.Core.Platform;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.Core.Actions;

[Flags]
public enum ActionKinds { None = 0, Discrete = 1, Continuous = 2, Hold = 4 }

public enum ParamKind { Text, Keys, Number, Path, Url, Process, Profile, Menu, Choice }

public sealed record ParamDef(string Key, string Label, ParamKind Kind, string Default = "", string[]? Choices = null, string Hint = "");

public sealed record ActionDescriptor(
    string Type, string Name, string Category, string Icon, string Description,
    ActionKinds Kinds, ParamDef[]? Params = null)
{
    public ParamDef[] ParamList => Params ?? Array.Empty<ParamDef>();
    public bool CanBeDiscrete => (Kinds & (ActionKinds.Discrete | ActionKinds.Hold)) != 0;
    public bool CanBeContinuous => (Kinds & ActionKinds.Continuous) != 0;
}

/// <summary>Callbacks from actions into the engine (profile switching, quick menu, notifications).</summary>
public interface IEngineControl
{
    void NextProfile();
    void PrevProfile();
    bool SwitchProfile(string idOrName);
    void ToggleActive();
    void OpenQuickMenu(string? menuId, bool holdMode);
    void ConfirmQuickMenu();
    void CancelQuickMenu();
    void Notify(string message);
}

/// <summary>Shared mutable state of the "grabbed" window (button hold -&gt; window follows the SpaceMouse).</summary>
public sealed class WindowGrabState
{
    public nint Handle;
}

public sealed class ActionContext
{
    public ActionContext(IPlatform platform, IEngineControl control, ActionRegistry registry)
    {
        Platform = platform; Control = control; Registry = registry;
    }
    public IPlatform Platform { get; }
    public IEngineControl Control { get; }
    public ActionRegistry Registry { get; }
    public WindowGrabState Grab { get; } = new();
    public CancellationTokenSource Cancel { get; } = new();
    public IInputSimulator Input => Platform.Input;
    public IWindowService Windows => Platform.Windows;
    public ISystemService System => Platform.System;
}

/// <summary>
/// One instantiated action. A handler lives as long as its binding is loaded, so continuous
/// handlers can keep sub-pixel accumulators.
/// </summary>
public abstract class ActionHandler
{
    /// <summary>Discrete trigger.</summary>
    public virtual void Execute() { }
    /// <summary>Hold start (button pressed).</summary>
    public virtual void Begin() => Execute();
    /// <summary>Hold end (button released).</summary>
    public virtual void End() { }
    /// <summary>Continuous binding became active (deflection left dead zone).</summary>
    public virtual void Activate() { }
    public virtual void Deactivate() { }
    /// <summary>Continuous update; <paramref name="value"/> is signed and may exceed 1 (speed multiplier).</summary>
    public virtual void Tick(double value, double dt) { }
}

public sealed class NullAction : ActionHandler { }

public sealed class ActionRegistry
{
    private readonly Dictionary<string, (ActionDescriptor Desc, Func<ActionDef, ActionContext, ActionHandler> Factory)> _map = new();
    private readonly List<string> _order = new();

    public void Register(ActionDescriptor d, Func<ActionDef, ActionContext, ActionHandler> factory)
    {
        if (!_map.ContainsKey(d.Type)) _order.Add(d.Type);
        _map[d.Type] = (d, factory);
    }

    public IEnumerable<ActionDescriptor> All => _order.Select(t => _map[t].Desc);
    public IEnumerable<string> Categories => All.Select(a => a.Category).Distinct();
    public ActionDescriptor? Find(string? type) => type != null && _map.TryGetValue(type, out var e) ? e.Desc : null;
    public bool Exists(string? type) => type != null && _map.ContainsKey(type);

    public ActionHandler Create(ActionDef? def, ActionContext ctx)
    {
        if (def == null || def.Type == "none" || !_map.TryGetValue(def.Type, out var e)) return new NullAction();
        return e.Factory(def, ctx);
    }

    /// <summary>Short human readable summary, e.g. "Send keys: Ctrl+W".</summary>
    public string Describe(ActionDef? def)
    {
        if (def == null || def.Type == "none") return "Nothing";
        var d = Find(def.Type);
        if (d == null) return $"Unknown action ({def.Type})";
        if (def.Type == "macro.sequence") return $"{(def.Params.GetValueOrDefault("name") is { Length: > 0 } n ? n : "Action sequence")} ({def.Steps.Count} steps)";
        var main = d.ParamList.FirstOrDefault();
        if (main != null && def.Params.TryGetValue(main.Key, out var v) && !string.IsNullOrWhiteSpace(v))
            return $"{d.Name}: {v}";
        return d.Name;
    }
}
