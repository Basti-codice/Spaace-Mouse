using SpaceMouse.Core.Input;

namespace SpaceMouse.Core.Profiles;

/// <summary>Reference to an action in the library plus its parameters. Macros nest further steps.</summary>
public sealed class ActionDef
{
    public string Type { get; set; } = "none";
    public Dictionary<string, string> Params { get; set; } = new();
    public List<ActionDef> Steps { get; set; } = new();

    public ActionDef() { }
    public ActionDef(string type, params (string, string)[] p)
    {
        Type = type;
        foreach (var (k, v) in p) Params[k] = v;
    }

    public string Get(string key, string fallback = "") => Params.TryGetValue(key, out var v) ? v : fallback;
    public double GetDouble(string key, double fallback)
        => Params.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : fallback;
    public int GetInt(string key, int fallback) => (int)Math.Round(GetDouble(key, fallback));

    public ActionDef Clone() => new()
    {
        Type = Type,
        Params = new Dictionary<string, string>(Params),
        Steps = Steps.Select(s => s.Clone()).ToList(),
    };
}

public enum ButtonModifier { None, Left, Right, Both }

public enum AxisBehavior { Continuous, Discrete }

/// <summary>Per-axis response tuning ("user sensitivity" - distinct from hardware calibration).</summary>
public sealed class AxisTuning
{
    public bool Invert { get; set; }
    /// <summary>Overall gain applied after the dead zone.</summary>
    public double Sensitivity { get; set; } = 1.0;
    /// <summary>Additional software dead zone (0..0.9).</summary>
    public double Deadzone { get; set; } = 0.08;
    /// <summary>Discrete: deflection needed to trigger (0..1).</summary>
    public double Threshold { get; set; } = 0.5;
    /// <summary>Continuous: speed multiplier of the action (action defines base unit).</summary>
    public double Speed { get; set; } = 1.0;
    /// <summary>Response curve exponent: 1 = linear, &gt;1 = fine control for small deflections, &lt;1 = aggressive.</summary>
    public double Acceleration { get; set; } = 1.5;
    /// <summary>Above this deflection the strong multiplier applies ("behaviour on strong movements").</summary>
    public double StrongThreshold { get; set; } = 0.85;
    public double StrongMultiplier { get; set; } = 1.0;
    /// <summary>Weak movements below this deflection are damped by <see cref="WeakDamping"/> (behaviour on weak movements).</summary>
    public double WeakZone { get; set; } = 0.0;
    public double WeakDamping { get; set; } = 1.0;
    /// <summary>Discrete: minimum time between two triggers.</summary>
    public int CooldownMs { get; set; } = 350;
    /// <summary>Discrete: keep firing while held beyond the threshold.</summary>
    public bool Repeat { get; set; }
    public int RepeatMs { get; set; } = 450;

    public AxisTuning Clone() => (AxisTuning)MemberwiseClone();
}

public sealed class AxisBinding
{
    public Axis Axis { get; set; }
    public ButtonModifier Modifier { get; set; } = ButtonModifier.None;
    public AxisBehavior Behavior { get; set; } = AxisBehavior.Continuous;
    /// <summary>Continuous action (receives signed value).</summary>
    public ActionDef? Action { get; set; }
    /// <summary>Discrete action for positive direction.</summary>
    public ActionDef? Positive { get; set; }
    /// <summary>Discrete action for negative direction.</summary>
    public ActionDef? Negative { get; set; }
    public AxisTuning Tuning { get; set; } = new();

    public AxisBinding Clone() => new()
    {
        Axis = Axis, Modifier = Modifier, Behavior = Behavior,
        Action = Action?.Clone(), Positive = Positive?.Clone(), Negative = Negative?.Clone(),
        Tuning = Tuning.Clone(),
    };
}

public enum ButtonMode
{
    /// <summary>Fires on release if it was a short press and the button was not used as a modifier.</summary>
    Tap,
    /// <summary>Fires immediately on press.</summary>
    Press,
    /// <summary>Begin on press, End on release (e.g. mouse button held, window grab, quick menu).</summary>
    Hold,
}

public sealed class ButtonBinding
{
    public ButtonId Button { get; set; }
    public ButtonMode Mode { get; set; } = ButtonMode.Tap;
    public ActionDef? Action { get; set; }
    /// <summary>Optional action fired instead of <see cref="Action"/> when the press lasted at least <see cref="LongPressMs"/> (Tap mode).</summary>
    public ActionDef? LongPress { get; set; }
    public int LongPressMs { get; set; } = 500;

    public ButtonBinding Clone() => new()
    {
        Button = Button, Mode = Mode, Action = Action?.Clone(), LongPress = LongPress?.Clone(), LongPressMs = LongPressMs,
    };
}

public sealed class MenuItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Label { get; set; } = "Item";
    public string Icon { get; set; } = "star";
    public ActionDef? Action { get; set; }
    public List<MenuItem> Children { get; set; } = new();
    public bool HasChildren => Children.Count > 0;

    public MenuItem Clone() => new()
    {
        Id = Id, Label = Label, Icon = Icon, Action = Action?.Clone(), Children = Children.Select(c => c.Clone()).ToList(),
    };
}

public enum MenuSelectMode { Direction, Rotation }

public sealed class QuickMenu
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Quick Menu";
    public List<MenuItem> Items { get; set; } = new();
    public MenuSelectMode SelectMode { get; set; } = MenuSelectMode.Direction;
    /// <summary>Direction mode: axes that point at an item.</summary>
    public Axis DirectionX { get; set; } = Axis.Tx;
    public Axis DirectionY { get; set; } = Axis.Ty;
    /// <summary>Rotation mode: axis that steps through items.</summary>
    public Axis RotateAxis { get; set; } = Axis.Rz;
    public double SelectDeadzone { get; set; } = 0.30;
    /// <summary>Pressing down on the cap (Tz negative) confirms.</summary>
    public bool ConfirmWithPush { get; set; }
    public int RotateStepMs { get; set; } = 220;

    public QuickMenu Clone() => new()
    {
        Id = Id, Name = Name, Items = Items.Select(i => i.Clone()).ToList(), SelectMode = SelectMode,
        DirectionX = DirectionX, DirectionY = DirectionY, RotateAxis = RotateAxis,
        SelectDeadzone = SelectDeadzone, ConfirmWithPush = ConfirmWithPush, RotateStepMs = RotateStepMs,
    };
}

/// <summary>Auto-switch rule: when a process is in the foreground, this profile is activated.</summary>
public sealed class AppRule
{
    public string ProcessName { get; set; } = "";
}

/// <summary>A complete behaviour set ("mode"): bindings, quick menus, auto-switch rules.</summary>
public sealed class Profile
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New profile";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "star";
    public string? PresetKey { get; set; }
    public List<AxisBinding> Axes { get; set; } = new();
    public List<ButtonBinding> Buttons { get; set; } = new();
    public List<QuickMenu> QuickMenus { get; set; } = new();
    public List<AppRule> AppRules { get; set; } = new();

    public Profile Clone() => new()
    {
        SchemaVersion = SchemaVersion, Id = Id, Name = Name, Description = Description, Icon = Icon, PresetKey = PresetKey,
        Axes = Axes.Select(a => a.Clone()).ToList(),
        Buttons = Buttons.Select(b => b.Clone()).ToList(),
        QuickMenus = QuickMenus.Select(q => q.Clone()).ToList(),
        AppRules = AppRules.Select(r => new AppRule { ProcessName = r.ProcessName }).ToList(),
    };

    /// <summary>Best match for the current modifier state, falling back to the unmodified binding.</summary>
    public AxisBinding? ResolveAxis(Axis axis, bool left, bool right)
    {
        var want = left && right ? ButtonModifier.Both : left ? ButtonModifier.Left : right ? ButtonModifier.Right : ButtonModifier.None;
        AxisBinding? exact = null, fallback = null;
        foreach (var b in Axes)
        {
            if (b.Axis != axis) continue;
            if (b.Modifier == want) exact = b;
            else if (b.Modifier == ButtonModifier.None) fallback = b;
        }
        // "Both" without a dedicated binding: try the single-button bindings before falling back to none.
        if (exact == null && want == ButtonModifier.Both)
            exact = Axes.FirstOrDefault(b => b.Axis == axis && b.Modifier == ButtonModifier.Left)
                    ?? Axes.FirstOrDefault(b => b.Axis == axis && b.Modifier == ButtonModifier.Right);
        return exact ?? fallback;
    }

    public ButtonBinding? ResolveButton(ButtonId id) => Buttons.FirstOrDefault(b => b.Button == id);
    public QuickMenu? FindMenu(string? id) => QuickMenus.FirstOrDefault(m => m.Id == id) ?? QuickMenus.FirstOrDefault();

    /// <summary>True if any axis binding uses this button as a modifier (the button's own Tap action is then suppressed while axes move).</summary>
    public bool UsesButtonAsModifier(ButtonId id) =>
        Axes.Any(a => (a.Action is { Type: not "none" } || a.Positive is { Type: not "none" } || a.Negative is { Type: not "none" })
                      && (a.Modifier == ButtonModifier.Both
                          || (id == ButtonId.Left ? a.Modifier == ButtonModifier.Left : a.Modifier == ButtonModifier.Right)));
}
