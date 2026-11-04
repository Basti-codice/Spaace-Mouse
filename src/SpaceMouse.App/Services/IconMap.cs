namespace SpaceMouse.App.Services;

/// <summary>Maps icon keys used by actions, menus and profiles to glyphs of "Segoe Fluent Icons" / "Segoe MDL2 Assets".</summary>
public static class IconMap
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["star"] = "", ["block"] = "", ["cursor"] = "", ["scroll"] = "", ["zoom"] = "", ["click"] = "",
        ["tab-next"] = "", ["tab-prev"] = "", ["close"] = "", ["add"] = "", ["undo"] = "", ["redo"] = "",
        ["back"] = "", ["forward"] = "", ["refresh"] = "", ["search"] = "", ["switch"] = "",
        ["minimize"] = "", ["maximize"] = "", ["restore"] = "", ["snap-left"] = "", ["snap-right"] = "",
        ["snap-top"] = "", ["snap-bottom"] = "", ["center"] = "", ["monitor"] = "", ["move"] = "", ["grab"] = "",
        ["desktop"] = "", ["taskview"] = "", ["windows"] = "", ["screenshot"] = "", ["lock"] = "",
        ["folder"] = "", ["settings"] = "", ["volume"] = "", ["mute"] = "", ["play"] = "", ["pause"] = "",
        ["next"] = "", ["prev"] = "", ["keyboard"] = "", ["copy"] = "", ["paste"] = "", ["select"] = "",
        ["save"] = "", ["enter"] = "", ["escape"] = "", ["launch"] = "", ["focus"] = "", ["web"] = "",
        ["macro"] = "", ["timer"] = "", ["profile"] = "", ["power"] = "", ["radial"] = "", ["note"] = "",
        ["calc"] = "", ["music"] = "", ["window"] = "", ["code"] = "", ["bolt"] = "", ["home"] = "",
        ["mail"] = "", ["camera"] = "", ["link"] = "", ["heart"] = "", ["calendar"] = "", ["document"] = "",
        ["info"] = "", ["warning"] = "", ["user"] = "", ["pin"] = "", ["play2"] = "", ["edit"] = "",
    };

    public static IReadOnlyList<string> Keys { get; } = Map.Keys.OrderBy(k => k).ToList();
    public static string Glyph(string? key) => key != null && Map.TryGetValue(key, out var g) ? g : "";
}
