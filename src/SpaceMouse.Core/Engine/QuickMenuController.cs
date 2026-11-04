using SpaceMouse.Core.Input;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.Core.Engine;

public sealed record QuickMenuView(
    QuickMenu Menu, IReadOnlyList<MenuItem> Items, int Selected, int CursorX, int CursorY, bool HoldMode, string Title, int Depth);

/// <summary>
/// State machine of the radial menu. UI-agnostic: it consumes normalised frames and raises events.
/// Selection: pointing direction (X/Y) or stepping with rotation; confirm: release / left button / push down.
/// </summary>
public sealed class QuickMenuController
{
    public const string BackId = "__back";
    public const double MaxOpenSeconds = 60;

    private readonly List<(List<MenuItem> Items, string Title)> _path = new();
    private List<MenuItem> _items = new();
    private double _opened, _nextStep;
    private bool _pushLatched = true;

    public bool IsOpen { get; private set; }
    public QuickMenu? Menu { get; private set; }
    public int Selected { get; private set; } = -1;
    public bool HoldMode { get; private set; }
    public IReadOnlyList<MenuItem> Items => _items;
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }

    public event Action<QuickMenuView>? Opened;
    public event Action<QuickMenuView>? Changed;
    /// <summary>Raised on close; argument is the confirmed leaf item or null when cancelled.</summary>
    public event Action<MenuItem?>? Closed;

    public QuickMenuView View => new(Menu!, _items, Selected, CursorX, CursorY, HoldMode,
        _path.Count > 0 ? _path[^1].Title : Menu?.Name ?? "", _path.Count);

    public bool Open(QuickMenu menu, bool holdMode, int cursorX, int cursorY, double now)
    {
        if (IsOpen || menu.Items.Count == 0) return false;
        Menu = menu; HoldMode = holdMode; CursorX = cursorX; CursorY = cursorY;
        _path.Clear(); _items = menu.Items.ToList(); Selected = -1; _opened = now; _nextStep = now;
        _pushLatched = true; // require releasing the push before it can confirm
        IsOpen = true;
        Opened?.Invoke(View);
        return true;
    }

    public void Update(InputFrame f, double now)
    {
        if (!IsOpen || Menu == null) return;
        if (now - _opened > MaxOpenSeconds) { Cancel(); return; }
        var n = _items.Count;
        if (n == 0) { Cancel(); return; }
        var old = Selected;

        if (Menu.SelectMode == MenuSelectMode.Direction)
        {
            double x = f[Menu.DirectionX], y = f[Menu.DirectionY];
            if (Math.Sqrt(x * x + y * y) < Menu.SelectDeadzone) Selected = -1;
            else
            {
                var ang = Math.Atan2(x, y); // 0 = up, clockwise
                if (ang < 0) ang += 2 * Math.PI;
                var sector = 2 * Math.PI / n;
                Selected = (int)Math.Round(ang / sector) % n;
            }
        }
        else
        {
            var v = f[Menu.RotateAxis];
            if (Math.Abs(v) >= Menu.SelectDeadzone && now >= _nextStep)
            {
                var dir = v > 0 ? 1 : -1;
                Selected = Selected < 0 ? (dir > 0 ? 0 : n - 1) : (Selected + dir + n) % n;
                // faster rotation -> faster stepping
                _nextStep = now + Menu.RotateStepMs / 1000.0 / (0.5 + Math.Abs(v));
            }
        }

        if (Menu.ConfirmWithPush)
        {
            var pushing = f[Axis.Tz] < -0.75;
            if (!pushing) _pushLatched = false;
            else if (!_pushLatched) { _pushLatched = true; Confirm(); return; }
        }

        if (old != Selected) Changed?.Invoke(View);
    }

    /// <summary>Confirm current selection. Nothing selected = cancel.</summary>
    public void Confirm()
    {
        if (!IsOpen) return;
        if (Selected < 0 || Selected >= _items.Count) { Cancel(); return; }
        var item = _items[Selected];
        if (item.Id == BackId)
        {
            _path.RemoveAt(_path.Count - 1);
            _items = _path.Count > 0 ? WithBack(_path[^1].Items) : Menu!.Items;
            Selected = -1;
            Changed?.Invoke(View);
            return;
        }
        if (item.HasChildren)
        {
            _path.Add((item.Children, item.Label));
            _items = WithBack(item.Children);
            Selected = -1;
            HoldMode = false; // button was released on a folder -> keep the menu open in sticky mode
            Changed?.Invoke(View);
            return;
        }
        Close(item);
    }

    public void Cancel() { if (IsOpen) Close(null); }

    /// <summary>Direct selection by index (mouse hover / UI tests).</summary>
    public void Select(int index)
    {
        if (!IsOpen || index < -1 || index >= _items.Count || index == Selected) return;
        Selected = index;
        Changed?.Invoke(View);
    }

    private static List<MenuItem> WithBack(List<MenuItem> children)
    {
        var l = new List<MenuItem> { new() { Id = BackId, Label = "Back", Icon = "back" } };
        l.AddRange(children);
        return l;
    }

    private void Close(MenuItem? item)
    {
        IsOpen = false;
        Selected = -1;
        _path.Clear();
        Closed?.Invoke(item);
    }
}
