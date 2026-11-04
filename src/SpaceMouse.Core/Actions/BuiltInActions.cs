using SpaceMouse.Core.Platform;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.Core.Actions;

/// <summary>The default action library. Add new actions by registering them in <see cref="Create"/> - nothing else needs to change.</summary>
public static class BuiltInActions
{
    public const string CatMouse = "Mouse", CatBrowser = "Browser", CatWindow = "Windows", CatSystem = "System",
        CatPrograms = "Programs", CatKeys = "Keyboard", CatMedia = "Media", CatControl = "SpaceMouse", CatCustom = "Custom";

    public static ActionRegistry Create()
    {
        var r = new ActionRegistry();
        var D = ActionKinds.Discrete; var C = ActionKinds.Continuous; var H = ActionKinds.Hold;

        r.Register(new("none", "Nothing", CatControl, "block", "Does nothing.", D | H | C), (_, _) => new NullAction());

        // ---------- Mouse ----------
        r.Register(new("mouse.move.x", "Move cursor horizontally", CatMouse, "cursor", "Moves the Windows cursor left/right.", C),
            (_, c) => new CursorMove(c, horizontal: true));
        r.Register(new("mouse.move.y", "Move cursor vertically", CatMouse, "cursor", "Moves the Windows cursor up/down.", C),
            (_, c) => new CursorMove(c, horizontal: false));
        r.Register(new("mouse.scroll.v", "Scroll up / down", CatMouse, "scroll", "Smooth vertical scrolling.", C),
            (_, c) => new Scroll(c, vertical: true));
        r.Register(new("mouse.scroll.h", "Scroll left / right", CatMouse, "scroll", "Smooth horizontal scrolling.", C),
            (_, c) => new Scroll(c, vertical: false));
        r.Register(new("mouse.zoom", "Zoom (Ctrl + wheel)", CatMouse, "zoom", "Zooms in browsers, PDF viewers and many editors.", C),
            (_, c) => new Zoom(c));
        r.Register(new("mouse.click.left", "Left click", CatMouse, "click", "Left mouse button. In hold mode it stays pressed (drag).", D | H),
            (_, c) => new MouseClick(c, MouseButton.Left));
        r.Register(new("mouse.click.right", "Right click", CatMouse, "click", "Right mouse button.", D | H),
            (_, c) => new MouseClick(c, MouseButton.Right));
        r.Register(new("mouse.click.middle", "Middle click", CatMouse, "click", "Mouse wheel click.", D | H),
            (_, c) => new MouseClick(c, MouseButton.Middle));
        r.Register(new("mouse.doubleclick", "Double click", CatMouse, "click", "Two quick left clicks.", D),
            (_, c) => new MouseClick(c, MouseButton.Left, double_: true));

        // ---------- Browser ----------
        Key(r, "browser.tab.next", "Next tab", CatBrowser, "tab-next", "Ctrl+Tab");
        Key(r, "browser.tab.prev", "Previous tab", CatBrowser, "tab-prev", "Ctrl+Shift+Tab");
        Key(r, "browser.tab.close", "Close tab", CatBrowser, "close", "Ctrl+W");
        Key(r, "browser.tab.new", "New tab", CatBrowser, "add", "Ctrl+T");
        Key(r, "browser.tab.restore", "Reopen closed tab", CatBrowser, "undo", "Ctrl+Shift+T");
        Key(r, "browser.back", "Back", CatBrowser, "back", "Alt+Left");
        Key(r, "browser.forward", "Forward", CatBrowser, "forward", "Alt+Right");
        Key(r, "browser.reload", "Reload page", CatBrowser, "refresh", "F5");
        Key(r, "browser.address", "Focus address bar", CatBrowser, "search", "Ctrl+L");

        // ---------- Windows ----------
        Key(r, "window.switch.next", "Switch window (Alt+Tab)", CatWindow, "switch", "Alt+Tab");
        Key(r, "window.switch.prev", "Switch window backwards", CatWindow, "switch", "Alt+Shift+Tab");
        r.Register(new("window.close", "Close window", CatWindow, "close", "Closes the active (or grabbed) window.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Close(h)));
        r.Register(new("window.minimize", "Minimise window", CatWindow, "minimize", "Minimises the active window.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Minimize(h)));
        r.Register(new("window.maximize", "Maximise window", CatWindow, "maximize", "Maximises the active window.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Maximize(h)));
        r.Register(new("window.restore", "Restore window", CatWindow, "restore", "Restores a maximised/minimised window.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Restore(h)));
        r.Register(new("window.snap.left", "Snap window left", CatWindow, "snap-left", "Docks the window to the left half.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Snap(h, SnapPosition.Left)));
        r.Register(new("window.snap.right", "Snap window right", CatWindow, "snap-right", "Docks the window to the right half.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Snap(h, SnapPosition.Right)));
        r.Register(new("window.snap.top", "Snap window to top half", CatWindow, "snap-top", "Docks the window to the top half.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Snap(h, SnapPosition.Top)));
        r.Register(new("window.snap.bottom", "Snap window to bottom half", CatWindow, "snap-bottom", "Docks the window to the bottom half.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Snap(h, SnapPosition.Bottom)));
        r.Register(new("window.snap.center", "Center window", CatWindow, "center", "Centers the window on its monitor.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.Snap(h, SnapPosition.Center)));
        r.Register(new("window.nextmonitor", "Move window to next monitor", CatWindow, "monitor", "Sends the window to the next display.", D),
            (_, c) => new WindowCommand(c, (w, h) => w.MoveToNextMonitor(h)));
        r.Register(new("window.move.x", "Move window horizontally", CatWindow, "move", "Moves the grabbed or active window left/right.", C),
            (_, c) => new WindowMove(c, horizontal: true));
        r.Register(new("window.move.y", "Move window vertically", CatWindow, "move", "Moves the grabbed or active window up/down.", C),
            (_, c) => new WindowMove(c, horizontal: false));
        r.Register(new("window.grab", "Grab window under cursor", CatWindow, "grab", "While held, window movement actions move the window under the cursor.", H),
            (_, c) => new WindowGrab(c));
        Key(r, "desktop.next", "Next virtual desktop", CatWindow, "desktop", "Ctrl+Win+Right");
        Key(r, "desktop.prev", "Previous virtual desktop", CatWindow, "desktop", "Ctrl+Win+Left");
        Key(r, "desktop.new", "New virtual desktop", CatWindow, "desktop", "Ctrl+Win+D");

        // ---------- System ----------
        Key(r, "system.showdesktop", "Show desktop", CatSystem, "desktop", "Win+D");
        Key(r, "system.taskview", "Task view", CatSystem, "taskview", "Win+Tab");
        Key(r, "system.startmenu", "Start menu", CatSystem, "windows", "Win");
        Key(r, "system.screenshot", "Screenshot (Snipping)", CatSystem, "screenshot", "Win+Shift+S");
        Key(r, "system.lock", "Lock PC", CatSystem, "lock", "Win+L");
        Key(r, "system.explorer", "Open File Explorer", CatSystem, "folder", "Win+E");
        Key(r, "system.search", "Search", CatSystem, "search", "Win+S");
        Key(r, "system.settings", "Settings", CatSystem, "settings", "Win+I");
        r.Register(new("system.volume", "Volume up / down", CatSystem, "volume", "Changes the system volume continuously.", C),
            (_, c) => new Volume(c));
        Key(r, "system.mute", "Mute", CatSystem, "mute", "VolumeMute");

        // ---------- Media ----------
        Key(r, "media.playpause", "Play / Pause", CatMedia, "play", "MediaPlay");
        Key(r, "media.next", "Next track", CatMedia, "next", "MediaNext");
        Key(r, "media.prev", "Previous track", CatMedia, "prev", "MediaPrev");

        // ---------- Keyboard ----------
        r.Register(new("keys.send", "Send keyboard shortcut", CatKeys, "keyboard", "Presses a key combination such as Ctrl+Shift+T. Use commas for several in a row.", D,
            new[] { new ParamDef("keys", "Keys", ParamKind.Keys, "Ctrl+C", Hint: "e.g. Ctrl+Shift+T or Ctrl+K, Ctrl+C") }),
            (d, c) => new KeysSend(c, d.Get("keys")));
        r.Register(new("keys.hold", "Hold keys while pressed", CatKeys, "keyboard", "Keeps keys pressed while the button is held (e.g. Shift, Ctrl).", H,
            new[] { new ParamDef("keys", "Keys", ParamKind.Keys, "Shift") }),
            (d, c) => new KeysHold(c, d.Get("keys")));
        Key(r, "edit.copy", "Copy", CatKeys, "copy", "Ctrl+C");
        Key(r, "edit.paste", "Paste", CatKeys, "paste", "Ctrl+V");
        Key(r, "edit.undo", "Undo", CatKeys, "undo", "Ctrl+Z");
        Key(r, "edit.redo", "Redo", CatKeys, "redo", "Ctrl+Y");
        Key(r, "edit.selectall", "Select all", CatKeys, "select", "Ctrl+A");
        Key(r, "edit.save", "Save", CatKeys, "save", "Ctrl+S");
        Key(r, "edit.enter", "Enter", CatKeys, "enter", "Enter");
        Key(r, "edit.escape", "Escape", CatKeys, "escape", "Esc");

        // ---------- Programs ----------
        r.Register(new("program.launch", "Launch program", CatPrograms, "launch", "Starts a program or file.", D,
            new[] { new ParamDef("path", "Program or file", ParamKind.Path, Hint: "e.g. notepad.exe"), new ParamDef("args", "Arguments", ParamKind.Text) }),
            (d, c) => new Launch(c, d));
        r.Register(new("program.focus", "Focus or launch program", CatPrograms, "focus", "Brings a running program to the front, or starts it.", D,
            new[] { new ParamDef("path", "Program", ParamKind.Process, Hint: "e.g. chrome or C:\\...\\app.exe"), new ParamDef("args", "Arguments", ParamKind.Text) }),
            (d, c) => new FocusProgram(c, d));
        r.Register(new("program.url", "Open website", CatPrograms, "web", "Opens a URL in the default browser.", D,
            new[] { new ParamDef("url", "Address", ParamKind.Url, "https://") }),
            (d, c) => new OpenUrl(c, d));

        // ---------- Custom ----------
        r.Register(new("macro.sequence", "Action sequence", CatCustom, "macro", "Runs several actions one after another.", D,
            new[] { new ParamDef("name", "Name", ParamKind.Text, "My sequence") }),
            (d, c) => new Macro(c, d));
        r.Register(new("macro.wait", "Wait", CatCustom, "timer", "Pause inside a sequence.", D,
            new[] { new ParamDef("ms", "Milliseconds", ParamKind.Number, "300") }),
            (d, c) => new NullAction());

        // ---------- SpaceMouse control ----------
        r.Register(new("profile.next", "Next profile", CatControl, "profile", "Switches to the next profile.", D),
            (_, c) => new Callback(() => c.Control.NextProfile()));
        r.Register(new("profile.prev", "Previous profile", CatControl, "profile", "Switches to the previous profile.", D),
            (_, c) => new Callback(() => c.Control.PrevProfile()));
        r.Register(new("profile.switch", "Switch to profile", CatControl, "profile", "Activates a specific profile.", D,
            new[] { new ParamDef("profile", "Profile", ParamKind.Profile) }),
            (d, c) => new Callback(() => c.Control.SwitchProfile(d.Get("profile"))));
        r.Register(new("engine.toggle", "Pause / resume SpaceMouse control", CatControl, "power", "Toggles between Standard (off) and Custom (active).", D),
            (_, c) => new Callback(() => c.Control.ToggleActive()));
        r.Register(new("quickmenu.open", "Open quick menu", CatControl, "radial", "Opens the radial menu. Confirm with left button, cancel with right button.", D,
            new[] { new ParamDef("menu", "Menu", ParamKind.Menu) }),
            (d, c) => new Callback(() => c.Control.OpenQuickMenu(d.Get("menu"), holdMode: false)));
        r.Register(new("quickmenu.hold", "Quick menu while held", CatControl, "radial", "Opens the radial menu while the button is held; releasing confirms.", H,
            new[] { new ParamDef("menu", "Menu", ParamKind.Menu) }),
            (d, c) => new HoldCallback(() => c.Control.OpenQuickMenu(d.Get("menu"), holdMode: true), () => c.Control.ConfirmQuickMenu()));
        return r;
    }

    private static void Key(ActionRegistry r, string type, string name, string cat, string icon, string keys)
        => r.Register(new(type, name, cat, icon, $"Sends {keys}.", ActionKinds.Discrete),
            (d, c) => new KeysSend(c, keys));

    // =============== handlers ===============

    private sealed class Callback : ActionHandler
    {
        private readonly Action _a;
        public Callback(Action a) { _a = a; }
        public override void Execute() => _a();
    }

    private sealed class HoldCallback : ActionHandler
    {
        private readonly Action _begin, _end;
        public HoldCallback(Action b, Action e) { _begin = b; _end = e; }
        public override void Execute() => _begin();
        public override void Begin() => _begin();
        public override void End() => _end();
    }

    private abstract class Accumulating : ActionHandler
    {
        protected double Acc;
        public override void Deactivate() => Acc = 0;
        protected int Take(double amount)
        {
            Acc += amount;
            var whole = (int)Math.Truncate(Acc);
            Acc -= whole;
            return whole;
        }
    }

    private sealed class CursorMove : Accumulating
    {
        public const double PixelsPerSecond = 1000;
        private readonly ActionContext _c; private readonly bool _h;
        public CursorMove(ActionContext c, bool horizontal) { _c = c; _h = horizontal; }
        public override void Tick(double value, double dt)
        {
            // + X = right, + Y (forward) = up on screen
            var px = Take((_h ? value : -value) * PixelsPerSecond * dt);
            if (px == 0) return;
            if (_h) _c.Input.MoveCursorBy(px, 0); else _c.Input.MoveCursorBy(0, px);
        }
    }

    private sealed class Scroll : Accumulating
    {
        public const double UnitsPerSecond = 1200; // 10 notches per second at full deflection
        private readonly ActionContext _c; private readonly bool _v;
        public Scroll(ActionContext c, bool vertical) { _c = c; _v = vertical; }
        public override void Tick(double value, double dt)
        {
            var u = Take(value * UnitsPerSecond * dt);
            if (u == 0) return;
            if (_v) _c.Input.Wheel(u); else _c.Input.HWheel(u);
        }
    }

    private sealed class Zoom : Accumulating
    {
        private readonly ActionContext _c;
        public Zoom(ActionContext c) { _c = c; }
        public override void Tick(double value, double dt)
        {
            var u = Take(value * 600 * dt);
            if (u == 0) return;
            _c.Input.KeyDown(KeyCombo.VK_CONTROL);
            try { _c.Input.Wheel(u); } finally { _c.Input.KeyUp(KeyCombo.VK_CONTROL); }
        }
    }

    private sealed class Volume : Accumulating
    {
        private readonly ActionContext _c;
        public Volume(ActionContext c) { _c = c; }
        public override void Tick(double value, double dt)
        {
            var steps = Take(value * 8 * dt);
            if (steps == 0) return;
            var vk = steps > 0 ? KeyCombo.VK_VOLUME_UP : KeyCombo.VK_VOLUME_DOWN;
            for (var i = 0; i < Math.Abs(steps); i++) { _c.Input.KeyDown(vk); _c.Input.KeyUp(vk); }
        }
    }

    private sealed class MouseClick : ActionHandler
    {
        private readonly ActionContext _c; private readonly MouseButton _b; private readonly bool _double; private bool _down;
        public MouseClick(ActionContext c, MouseButton b, bool double_ = false) { _c = c; _b = b; _double = double_; }
        public override void Execute()
        {
            var n = _double ? 2 : 1;
            for (var i = 0; i < n; i++) { _c.Input.MouseDown(_b); _c.Input.MouseUp(_b); }
        }
        public override void Begin() { if (_down) return; _down = true; _c.Input.MouseDown(_b); }
        public override void End() { if (!_down) return; _down = false; _c.Input.MouseUp(_b); }
    }

    private sealed class KeysSend : ActionHandler
    {
        private readonly ActionContext _c; private readonly List<ushort[]>? _seq;
        public KeysSend(ActionContext c, string keys) { _c = c; _seq = KeyCombo.ParseSequence(keys); }
        public override void Execute()
        {
            if (_seq == null) throw new InvalidOperationException("Invalid key combination.");
            foreach (var chord in _seq) KeyCombo.Send(_c.Input, chord);
        }
    }

    private sealed class KeysHold : ActionHandler
    {
        private readonly ActionContext _c; private readonly ushort[]? _chord; private bool _down;
        public KeysHold(ActionContext c, string keys) { _c = c; _chord = KeyCombo.ParseChord(keys); }
        public override void Begin()
        {
            if (_chord == null || _down) return;
            _down = true;
            foreach (var k in _chord) _c.Input.KeyDown(k);
        }
        public override void End()
        {
            if (_chord == null || !_down) return;
            _down = false;
            for (var i = _chord.Length - 1; i >= 0; i--) _c.Input.KeyUp(_chord[i]);
        }
    }

    private static nint TargetWindow(ActionContext c)
    {
        if (c.Grab.Handle != 0) return c.Grab.Handle;
        var (x, y) = c.Input.GetCursor();
        var under = c.Windows.WindowAt(x, y);
        return (under ?? c.Windows.GetForeground())?.Handle ?? 0;
    }

    private sealed class WindowCommand : ActionHandler
    {
        private readonly ActionContext _c; private readonly Action<IWindowService, nint> _a;
        public WindowCommand(ActionContext c, Action<IWindowService, nint> a) { _c = c; _a = a; }
        public override void Execute()
        {
            // Prefer the grabbed window, else the foreground window (window commands are about the active window).
            var h = _c.Grab.Handle != 0 ? _c.Grab.Handle : _c.Windows.GetForeground()?.Handle ?? 0;
            if (h == 0) throw new InvalidOperationException("No window available.");
            _a(_c.Windows, h);
        }
    }

    private sealed class WindowMove : Accumulating
    {
        public const double PixelsPerSecond = 1200;
        private readonly ActionContext _c; private readonly bool _h; private nint _target;
        public WindowMove(ActionContext c, bool horizontal) { _c = c; _h = horizontal; }
        public override void Activate() { _target = TargetWindow(_c); }
        public override void Deactivate() { base.Deactivate(); _target = 0; }
        public override void Tick(double value, double dt)
        {
            if (_target == 0) _target = TargetWindow(_c);
            if (_target == 0) return;
            var px = Take((_h ? value : -value) * PixelsPerSecond * dt);
            if (px == 0) return;
            if (_h) _c.Windows.MoveBy(_target, px, 0); else _c.Windows.MoveBy(_target, 0, px);
        }
    }

    private sealed class WindowGrab : ActionHandler
    {
        private readonly ActionContext _c;
        public WindowGrab(ActionContext c) { _c = c; }
        public override void Begin()
        {
            var (x, y) = _c.Input.GetCursor();
            _c.Grab.Handle = (_c.Windows.WindowAt(x, y) ?? _c.Windows.GetForeground())?.Handle ?? 0;
        }
        public override void End() => _c.Grab.Handle = 0;
    }

    private sealed class Launch : ActionHandler
    {
        private readonly ActionContext _c; private readonly ActionDef _d;
        public Launch(ActionContext c, ActionDef d) { _c = c; _d = d; }
        public override void Execute()
        {
            var p = _d.Get("path");
            if (string.IsNullOrWhiteSpace(p)) throw new InvalidOperationException("No program configured.");
            _c.System.Launch(p, _d.Get("args"));
        }
    }

    private sealed class FocusProgram : ActionHandler
    {
        private readonly ActionContext _c; private readonly ActionDef _d;
        public FocusProgram(ActionContext c, ActionDef d) { _c = c; _d = d; }
        public override void Execute()
        {
            var p = _d.Get("path");
            if (string.IsNullOrWhiteSpace(p)) throw new InvalidOperationException("No program configured.");
            if (!_c.System.FocusOrLaunch(p, _d.Get("args"))) throw new InvalidOperationException($"Could not focus or start '{p}'.");
        }
    }

    private sealed class OpenUrl : ActionHandler
    {
        private readonly ActionContext _c; private readonly ActionDef _d;
        public OpenUrl(ActionContext c, ActionDef d) { _c = c; _d = d; }
        public override void Execute()
        {
            var u = _d.Get("url");
            if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new InvalidOperationException("Only http(s) addresses can be opened.");
            _c.System.Launch(uri.AbsoluteUri);
        }
    }

    private sealed class Macro : ActionHandler
    {
        private readonly ActionContext _c; private readonly ActionDef _d;
        public Macro(ActionContext c, ActionDef d) { _c = c; _d = d; }
        public override void Execute()
        {
            var steps = _d.Steps.Select(s => s.Clone()).ToList();
            var ct = _c.Cancel.Token;
            Task.Run(async () =>
            {
                foreach (var s in steps)
                {
                    if (ct.IsCancellationRequested) return;
                    if (s.Type == "macro.wait") { await Task.Delay(Math.Clamp(s.GetInt("ms", 300), 0, 10000), ct).ConfigureAwait(false); continue; }
                    if (s.Type == "macro.sequence") continue; // no recursion
                    try { _c.Registry.Create(s, _c).Execute(); }
                    catch (Exception e) { _c.Control.Notify($"Sequence step failed: {e.Message}"); return; }
                    await Task.Delay(40, ct).ConfigureAwait(false);
                }
            }, ct).ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
        }
    }
}
