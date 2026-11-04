using SpaceMouse.Core.Input;

namespace SpaceMouse.Core.Hid;

/// <summary>
/// Turns raw HID reports into raw slot values and normalised <see cref="InputFrame"/>s using a <see cref="HardwareProfile"/>.
/// Pure and deterministic - fully unit-testable without hardware.
/// </summary>
public sealed class ReportDecoder
{
    private readonly double[] _slots = new double[6];
    private bool _left, _right;

    public ReportDecoder(HardwareProfile profile) { Profile = profile; }

    public HardwareProfile Profile { get; set; }

    /// <summary>Latest raw value per slot (un-normalised, as sent by the device).</summary>
    public IReadOnlyList<double> RawSlots => _slots;
    public bool RawLeft => _left;
    public bool RawRight => _right;

    /// <summary>Returns true when the report matched a known layout and state changed or was refreshed.</summary>
    public bool Decode(byte[] data)
    {
        if (data.Length == 0) return false;
        var layout = Profile.Reports.FirstOrDefault(r => r.ReportId == data[0]);
        if (layout == null) return false;
        var baseOff = layout.ReportId == 0 ? 0 : 1;

        // Validate the whole report first so a truncated report never half-updates the state.
        foreach (var f in layout.Fields)
        {
            var need = f.Type is FieldType.Int16LE or FieldType.Int16BE ? 2 : 1;
            if (f.Slot < 0 || f.Slot > 5 || baseOff + f.Offset + need > data.Length) return false;
        }
        if (layout.Buttons is { } bf && baseOff + bf.Offset + Math.Max(bf.LeftBit, bf.RightBit) / 8 >= data.Length) return false;

        foreach (var f in layout.Fields)
        {
            var o = baseOff + f.Offset;
            _slots[f.Slot] = f.Type switch
            {
                FieldType.Int16LE => (short)(data[o] | data[o + 1] << 8),
                FieldType.Int16BE => (short)(data[o] << 8 | data[o + 1]),
                FieldType.Int8 => (sbyte)data[o],
                _ => data[o],
            };
        }

        if (layout.Buttons is { } b)
        {
            var o = baseOff + b.Offset;
            if (o >= data.Length) return false;
            _left = Bit(data, o, b.LeftBit);
            _right = Bit(data, o, b.RightBit);
        }
        return true;
    }

    public void Reset()
    {
        Array.Clear(_slots);
        _left = _right = false;
    }

    public InputFrame CurrentFrame()
    {
        var axes = new double[6];
        for (var i = 0; i < 6; i++) axes[i] = Normalize(Profile.Axes[i], _slots);
        return new InputFrame(axes, _left, _right);
    }

    public static double Normalize(AxisCalibration c, IReadOnlyList<double> slots)
    {
        var raw = c.Slot is >= 0 and <= 5 ? slots[c.Slot] : 0;
        var range = Math.Max(1e-6, Math.Abs(c.Range));
        var n = (raw - c.Rest) / range;
        var mag = Math.Min(1.0, Math.Abs(n));
        var dz = Math.Clamp(c.Deadzone, 0, 0.95);
        mag = mag <= dz ? 0 : (mag - dz) / (1 - dz);
        mag = Math.Min(1.0, mag * c.Gain);
        var v = Math.Sign(n) * mag;
        return c.Invert ? -v : v;
    }

    private static bool Bit(byte[] d, int offset, int bit)
    {
        var byteIdx = offset + bit / 8;
        return byteIdx < d.Length && (d[byteIdx] >> (bit % 8) & 1) == 1;
    }
}
