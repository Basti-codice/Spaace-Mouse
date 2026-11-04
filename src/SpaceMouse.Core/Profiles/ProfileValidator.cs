using SpaceMouse.Core.Actions;

namespace SpaceMouse.Core.Profiles;

public sealed record ValidationIssue(string Message, bool IsFatal);

/// <summary>Detects broken profiles (unknown actions, invalid key strings, bad numbers) before they can misbehave.</summary>
public static class ProfileValidator
{
    public static List<ValidationIssue> Validate(Profile p, ActionRegistry reg)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(p.Name)) issues.Add(new("Profile has no name.", true));

        void Check(ActionDef? a, string where)
        {
            if (a == null || a.Type == "none") return;
            if (!reg.Exists(a.Type)) { issues.Add(new($"{where}: unknown action '{a.Type}'.", false)); return; }
            if (a.Type is "keys.send" or "keys.hold" && KeyCombo.ParseSequence(a.Get("keys")) == null)
                issues.Add(new($"{where}: invalid key combination '{a.Get("keys")}'.", false));
            foreach (var s in a.Steps) Check(s, where + " > step");
        }

        foreach (var ax in p.Axes)
        {
            var w = $"Axis {ax.Axis}";
            Check(ax.Action, w); Check(ax.Positive, w); Check(ax.Negative, w);
            var t = ax.Tuning;
            if (double.IsNaN(t.Sensitivity) || t.Sensitivity < 0 || t.Sensitivity > 20) issues.Add(new($"{w}: sensitivity out of range.", false));
            if (t.Deadzone is < 0 or > 0.95) issues.Add(new($"{w}: dead zone out of range.", false));
        }
        foreach (var b in p.Buttons) { Check(b.Action, $"Button {b.Button}"); Check(b.LongPress, $"Button {b.Button}"); }
        void Menu(IEnumerable<MenuItem> items, string where)
        {
            foreach (var i in items) { Check(i.Action, $"{where} / {i.Label}"); Menu(i.Children, $"{where} / {i.Label}"); }
        }
        foreach (var m in p.QuickMenus) Menu(m.Items, m.Name);
        return issues;
    }
}
