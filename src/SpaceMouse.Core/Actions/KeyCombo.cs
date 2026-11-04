namespace SpaceMouse.Core.Actions;

/// <summary>Parses strings like "Ctrl+Shift+T" or "Ctrl+K, Ctrl+C" into virtual-key chords.</summary>
public static class KeyCombo
{
    public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12,
        VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF,
        VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY = 0xB3, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;

    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VK_CONTROL, ["control"] = VK_CONTROL, ["shift"] = VK_SHIFT, ["alt"] = VK_MENU,
        ["win"] = VK_LWIN, ["windows"] = VK_LWIN, ["super"] = VK_LWIN, ["cmd"] = VK_LWIN,
        ["tab"] = VK_TAB, ["enter"] = VK_RETURN, ["return"] = VK_RETURN, ["esc"] = VK_ESCAPE, ["escape"] = VK_ESCAPE,
        ["space"] = VK_SPACE, ["backspace"] = VK_BACK, ["delete"] = 0x2E, ["del"] = 0x2E, ["insert"] = 0x2D,
        ["home"] = 0x24, ["end"] = 0x23, ["pageup"] = 0x21, ["pagedown"] = 0x22, ["pgup"] = 0x21, ["pgdn"] = 0x22,
        ["left"] = VK_LEFT, ["up"] = VK_UP, ["right"] = VK_RIGHT, ["down"] = VK_DOWN,
        ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["pause"] = 0x13, ["apps"] = 0x5D,
        ["volumeup"] = VK_VOLUME_UP, ["volumedown"] = VK_VOLUME_DOWN, ["volumemute"] = VK_VOLUME_MUTE,
        ["medianext"] = VK_MEDIA_NEXT, ["mediaprev"] = VK_MEDIA_PREV, ["mediaplay"] = VK_MEDIA_PLAY,
        ["plus"] = 0xBB, ["minus"] = 0xBD, ["comma"] = 0xBC, ["period"] = 0xBE,
    };

    public static bool IsModifier(ushort vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN;

    /// <summary>Parses one chord ("Ctrl+Shift+T"). Returns null if any token is unknown.</summary>
    public static ushort[]? ParseChord(string chord)
    {
        var keys = new List<ushort>();
        foreach (var raw in chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryKey(raw, out var vk)) return null;
            keys.Add(vk);
        }
        return keys.Count == 0 ? null : keys.ToArray();
    }

    /// <summary>Parses a sequence of chords separated by commas. Returns null on any error.</summary>
    public static List<ushort[]>? ParseSequence(string text)
    {
        var res = new List<ushort[]>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var c = ParseChord(part);
            if (c == null) return null;
            res.Add(c);
        }
        return res.Count == 0 ? null : res;
    }

    public static bool TryKey(string token, out ushort vk)
    {
        vk = 0;
        if (Names.TryGetValue(token, out vk)) return true;
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
        }
        if ((token[0] is 'f' or 'F') && int.TryParse(token.AsSpan(1), out var n) && n is >= 1 and <= 24) { vk = (ushort)(0x6F + n); return true; }
        return false;
    }

    /// <summary>Presses modifiers first (in given order), taps the rest, releases in reverse. Releases everything even on failure.</summary>
    public static void Send(Platform.IInputSimulator sim, ushort[] chord)
    {
        var down = new List<ushort>();
        try
        {
            foreach (var k in chord) { sim.KeyDown(k); down.Add(k); }
        }
        finally
        {
            for (var i = down.Count - 1; i >= 0; i--) sim.KeyUp(down[i]);
        }
    }
}
