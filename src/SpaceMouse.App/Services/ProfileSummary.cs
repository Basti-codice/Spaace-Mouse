using SpaceMouse.Core.Input;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.App.Services;

/// <summary>Plain-language descriptions of what a profile does - used by the device illustration and combo lists.</summary>
public static class ProfileSummary
{
    public static string AxisText(Profile? p, Axis axis)
    {
        if (p == null) return "-";
        var reg = AppController.Instance.Registry;
        var main = p.Axes.FirstOrDefault(a => a.Axis == axis && a.Modifier == ButtonModifier.None);
        var extra = p.Axes.Count(a => a.Axis == axis && a.Modifier != ButtonModifier.None && HasAction(a));
        string text;
        if (main == null || !HasAction(main)) text = extra > 0 ? "Only with buttons" : "Not assigned";
        else text = Describe(main, reg);
        if (extra > 0 && main != null && HasAction(main)) text += $"  +{extra} combo";
        return text;
    }

    public static string Describe(AxisBinding b, Core.Actions.ActionRegistry reg)
    {
        if (b.Behavior == AxisBehavior.Continuous) return reg.Describe(b.Action);
        var parts = new List<string>();
        if (b.Positive is { Type: not "none" }) parts.Add("+ " + reg.Describe(b.Positive));
        if (b.Negative is { Type: not "none" }) parts.Add("- " + reg.Describe(b.Negative));
        return parts.Count == 0 ? "Not assigned" : string.Join("   ", parts);
    }

    public static bool HasAction(AxisBinding b) =>
        b.Behavior == AxisBehavior.Continuous ? b.Action is { Type: not "none" } : b.Positive is { Type: not "none" } || b.Negative is { Type: not "none" };

    public static string Button(Profile? p, ButtonId id)
    {
        var b = p?.ResolveButton(id);
        if (b?.Action == null || b.Action.Type == "none") return "Not assigned";
        var reg = AppController.Instance.Registry;
        var text = reg.Describe(b.Action);
        if (b.LongPress is { Type: not "none" } && b.Mode == ButtonMode.Tap) text += " · hold: " + reg.Describe(b.LongPress);
        return text;
    }

    public static string ModifierName(ButtonModifier m) => m switch
    {
        ButtonModifier.Left => "Left button",
        ButtonModifier.Right => "Right button",
        ButtonModifier.Both => "Both buttons",
        _ => "Always",
    };

    /// <summary>All combinations (axis bindings that need a held button), e.g. "Left + Slide: Next virtual desktop".</summary>
    public static IEnumerable<(string Target, ButtonModifier Mod, string Text)> Combinations(Profile? p)
    {
        if (p == null) yield break;
        var reg = AppController.Instance.Registry;
        foreach (var a in p.Axes.Where(a => a.Modifier != ButtonModifier.None && HasAction(a)))
            yield return (a.Axis.ToString(), a.Modifier, $"{ModifierName(a.Modifier)} + {Core.Input.AxisInfo.Name(a.Axis)}  →  {Describe(a, reg)}");
    }
}
