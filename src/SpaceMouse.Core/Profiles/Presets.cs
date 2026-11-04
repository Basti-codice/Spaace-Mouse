using SpaceMouse.Core.Input;

namespace SpaceMouse.Core.Profiles;

/// <summary>Built-in, fully functional starter profiles. Users can duplicate and edit them; "Reset" restores the original.</summary>
public static class Presets
{
    public const string Desktop = "desktop", Browser = "browser", Productivity = "productivity", WindowManager = "windowmanager", Custom = "custom";
    public static readonly string[] Keys = { Desktop, Browser, Productivity, WindowManager, Custom };

    private static ActionDef A(string type, params (string, string)[] p) => new(type, p);

    private static AxisBinding Cont(Axis ax, string action, double speed = 1, double accel = 1.6, double dz = 0.08, bool invert = false,
        ButtonModifier mod = ButtonModifier.None) => new()
    {
        Axis = ax, Modifier = mod, Behavior = AxisBehavior.Continuous, Action = A(action),
        Tuning = new AxisTuning { Speed = speed, Acceleration = accel, Deadzone = dz, Invert = invert },
    };

    private static AxisBinding Disc(Axis ax, ActionDef? pos, ActionDef? neg, double thr = 0.55, int cooldown = 380,
        ButtonModifier mod = ButtonModifier.None, bool repeat = false) => new()
    {
        Axis = ax, Modifier = mod, Behavior = AxisBehavior.Discrete, Positive = pos, Negative = neg,
        Tuning = new AxisTuning { Threshold = thr, CooldownMs = cooldown, Deadzone = 0.1, Repeat = repeat, RepeatMs = 500 },
    };

    private static ButtonBinding Btn(ButtonId id, ButtonMode mode, ActionDef action, ActionDef? longPress = null) => new()
        { Button = id, Mode = mode, Action = action, LongPress = longPress };

    private static MenuItem Item(string label, string icon, ActionDef? action, params MenuItem[] children) => new()
        { Label = label, Icon = icon, Action = action, Children = children.ToList() };

    private static QuickMenu Menu(string name, params MenuItem[] items) => new() { Name = name, Items = items.ToList() };

    public static Profile Create(string key) => key switch
    {
        Desktop => MakeDesktop(),
        Browser => MakeBrowser(),
        Productivity => MakeProductivity(),
        WindowManager => MakeWindowManager(),
        _ => MakeCustom(),
    };

    public static List<Profile> All() => Keys.Select(Create).ToList();

    private static Profile Base(string key, string name, string desc, string icon) => new()
    {
        PresetKey = key, Name = name, Description = desc, Icon = icon,
        // Stable ids so presets keep their identity across app restarts and resets.
        Id = new Guid($"5ba5e000-0000-4000-8000-{Array.IndexOf(Keys, key):D12}"),
    };

    private static Profile MakeDesktop()
    {
        var p = Base(Desktop, "Standard Desktop", "Move the cursor with the cap, click with the buttons, twist to scroll.", "desktop");
        p.Axes.AddRange(new[]
        {
            Cont(Axis.Tx, "mouse.move.x", speed: 1.0),
            Cont(Axis.Ty, "mouse.move.y", speed: 1.0),
            Cont(Axis.Rz, "mouse.scroll.v", speed: 1.0, accel: 1.4),
            Disc(Axis.Ry, A("window.switch.next"), A("window.switch.prev"), thr: 0.6, cooldown: 450),
            Disc(Axis.Rx, A("system.taskview"), A("system.showdesktop"), thr: 0.7, cooldown: 700),
        });
        // Left/Right clicks use Hold mode so dragging works.
        p.Buttons.Add(Btn(ButtonId.Left, ButtonMode.Hold, A("mouse.click.left")));
        p.Buttons.Add(Btn(ButtonId.Right, ButtonMode.Hold, A("mouse.click.right")));
        p.QuickMenus.Add(Menu("Desktop",
            Item("Browser", "web", A("program.url", ("url", "https://www.google.com"))),
            Item("Explorer", "folder", A("system.explorer")),
            Item("Screenshot", "screenshot", A("system.screenshot")),
            Item("Task view", "taskview", A("system.taskview")),
            Item("Show desktop", "desktop", A("system.showdesktop")),
            Item("Settings", "settings", A("system.settings")),
            Item("Mute", "mute", A("system.mute")),
            Item("Lock", "lock", A("system.lock"))));
        return p;
    }

    private static Profile MakeBrowser()
    {
        var p = Base(Browser, "Browser", "Twist to switch tabs, push to go back/forward, scroll by tilting.", "web");
        p.Axes.AddRange(new[]
        {
            Disc(Axis.Rz, A("browser.tab.next"), A("browser.tab.prev"), thr: 0.5, cooldown: 350),
            Cont(Axis.Ty, "mouse.scroll.v", speed: 1.2, accel: 1.5),
            Disc(Axis.Tx, A("browser.forward"), A("browser.back"), thr: 0.7, cooldown: 600),
            Disc(Axis.Tz, A("browser.tab.new"), A("browser.tab.close"), thr: 0.75, cooldown: 700),
            Disc(Axis.Rx, A("browser.reload"), null, thr: 0.7, cooldown: 800),
            Cont(Axis.Ty, "mouse.zoom", speed: 0.5, mod: ButtonModifier.Left),
        });
        p.Buttons.Add(Btn(ButtonId.Left, ButtonMode.Tap, A("mouse.click.left")));
        p.Buttons.Add(Btn(ButtonId.Right, ButtonMode.Hold, A("quickmenu.hold")));
        p.QuickMenus.Add(Menu("Browser",
            Item("New tab", "add", A("browser.tab.new")),
            Item("Reopen tab", "undo", A("browser.tab.restore")),
            Item("Reload", "refresh", A("browser.reload")),
            Item("Close tab", "close", A("browser.tab.close")),
            Item("Back", "back", A("browser.back")),
            Item("Address bar", "search", A("browser.address")),
            Item("Forward", "forward", A("browser.forward")),
            Item("Sites", "web", null,
                Item("Google", "web", A("program.url", ("url", "https://www.google.com"))),
                Item("YouTube", "play", A("program.url", ("url", "https://www.youtube.com"))),
                Item("GitHub", "code", A("program.url", ("url", "https://github.com"))))));
        return p;
    }

    private static Profile MakeProductivity()
    {
        var p = Base(Productivity, "Productivity", "Switch apps and desktops, launch your tools from the quick menu.", "bolt");
        p.Axes.AddRange(new[]
        {
            Cont(Axis.Tx, "mouse.move.x"),
            Cont(Axis.Ty, "mouse.move.y"),
            Cont(Axis.Rz, "mouse.scroll.v", accel: 1.4),
            Disc(Axis.Ry, A("window.switch.next"), A("window.switch.prev"), thr: 0.6, cooldown: 450),
            Disc(Axis.Tx, A("desktop.next"), A("desktop.prev"), thr: 0.6, cooldown: 600, mod: ButtonModifier.Left),
            Disc(Axis.Ty, A("window.maximize"), A("window.minimize"), thr: 0.7, cooldown: 700, mod: ButtonModifier.Left),
            Disc(Axis.Rz, A("window.snap.right"), A("window.snap.left"), thr: 0.6, cooldown: 600, mod: ButtonModifier.Left),
            Disc(Axis.Tz, A("system.taskview"), A("system.showdesktop"), thr: 0.8, cooldown: 800),
        });
        // Movement axes without modifier keep the cursor usable; Left is a modifier here, so it uses Tap for clicks.
        p.Buttons.Add(Btn(ButtonId.Left, ButtonMode.Tap, A("mouse.click.left"), longPress: A("mouse.click.right")));
        p.Buttons.Add(Btn(ButtonId.Right, ButtonMode.Hold, A("quickmenu.hold")));
        p.QuickMenus.Add(Menu("Productivity",
            Item("Browser", "web", A("program.focus", ("path", "chrome"))),
            Item("Explorer", "folder", A("system.explorer")),
            Item("Notes", "note", A("program.focus", ("path", "notepad"))),
            Item("Calculator", "calc", A("program.focus", ("path", "calc"))),
            Item("Screenshot", "screenshot", A("system.screenshot")),
            Item("Music", "music", null,
                Item("Play / Pause", "play", A("media.playpause")),
                Item("Next", "next", A("media.next")),
                Item("Previous", "prev", A("media.prev"))),
            Item("Windows", "window", null,
                Item("Snap left", "snap-left", A("window.snap.left")),
                Item("Snap right", "snap-right", A("window.snap.right")),
                Item("Maximise", "maximize", A("window.maximize")),
                Item("Next monitor", "monitor", A("window.nextmonitor"))),
            Item("Task view", "taskview", A("system.taskview"))));
        return p;
    }

    private static Profile MakeWindowManager()
    {
        var p = Base(WindowManager, "Window Manager", "Hold the left button to grab a window and move it. Tilt to maximise, minimise or snap.", "window");
        p.Axes.AddRange(new[]
        {
            Cont(Axis.Tx, "window.move.x", speed: 1.0, accel: 1.4, mod: ButtonModifier.Left),
            Cont(Axis.Ty, "window.move.y", speed: 1.0, accel: 1.4, mod: ButtonModifier.Left),
            Cont(Axis.Tx, "mouse.move.x"),
            Cont(Axis.Ty, "mouse.move.y"),
            Disc(Axis.Ry, A("window.snap.right"), A("window.snap.left"), thr: 0.6, cooldown: 600),
            Disc(Axis.Rx, A("window.maximize"), A("window.minimize"), thr: 0.65, cooldown: 700),
            Disc(Axis.Rz, A("window.nextmonitor"), A("window.restore"), thr: 0.65, cooldown: 800),
            Disc(Axis.Tz, A("window.snap.center"), A("window.close"), thr: 0.9, cooldown: 1000, mod: ButtonModifier.Right),
        });
        p.Buttons.Add(Btn(ButtonId.Left, ButtonMode.Hold, A("window.grab")));
        p.Buttons.Add(Btn(ButtonId.Right, ButtonMode.Tap, A("mouse.click.left")));
        p.QuickMenus.Add(Menu("Windows",
            Item("Maximise", "maximize", A("window.maximize")),
            Item("Snap right", "snap-right", A("window.snap.right")),
            Item("Minimise", "minimize", A("window.minimize")),
            Item("Snap left", "snap-left", A("window.snap.left")),
            Item("Next monitor", "monitor", A("window.nextmonitor")),
            Item("Restore", "restore", A("window.restore")),
            Item("Close", "close", A("window.close")),
            Item("Desktop", "desktop", A("system.showdesktop"))));
        return p;
    }

    private static Profile MakeCustom()
    {
        var p = Base(Custom, "Custom", "An empty canvas - assign every axis and button yourself.", "star");
        p.QuickMenus.Add(new QuickMenu { Name = "My menu" });
        return p;
    }
}
