using SpaceMouse.Core.Input;

namespace SpaceMouse.Core.Hid;

public enum FieldType { Int16LE, Int16BE, Int8, UInt8 }

/// <summary>One numeric field inside a report; feeds a raw "slot" (0..5) that calibration maps to a logical axis.</summary>
public sealed class FieldDef
{
    public int Offset { get; set; }          // byte offset after the report id
    public FieldType Type { get; set; } = FieldType.Int16LE;
    public int Slot { get; set; }            // raw slot 0..5
}

public sealed class ButtonField
{
    public int Offset { get; set; }          // byte offset after the report id
    public int LeftBit { get; set; } = 0;
    public int RightBit { get; set; } = 1;
}

public sealed class ReportLayout
{
    public byte ReportId { get; set; }
    public List<FieldDef> Fields { get; set; } = new();
    public ButtonField? Buttons { get; set; }
}

/// <summary>How one logical axis is derived from a raw slot.</summary>
public sealed class AxisCalibration
{
    public int Slot { get; set; }
    public bool Invert { get; set; }
    public double Rest { get; set; }         // raw value at rest
    public double Range { get; set; } = 350; // max raw deflection (absolute)
    public double Deadzone { get; set; } = 0.02; // hardware level, fraction of range
    public double Gain { get; set; } = 1.0;  // hardware level scaling

    public AxisCalibration Clone() => (AxisCalibration)MemberwiseClone();
}

/// <summary>
/// Describes one hardware generation / variant: report layouts + per-axis calibration.
/// Everything above the InputHub only sees normalised frames.
/// </summary>
public sealed class HardwareProfile
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Unnamed hardware profile";
    public int? VendorId { get; set; }
    public int? ProductId { get; set; }
    public List<ReportLayout> Reports { get; set; } = new();
    /// <summary>Indexed by logical <see cref="Axis"/>.</summary>
    public AxisCalibration[] Axes { get; set; } = Enumerable.Range(0, 6).Select(i => new AxisCalibration { Slot = i }).ToArray();
    /// <summary>True once the user finished the calibration wizard for this profile.</summary>
    public bool Calibrated { get; set; }
    public string Notes { get; set; } = "";

    public HardwareProfile Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        var c = System.Text.Json.JsonSerializer.Deserialize<HardwareProfile>(json)!;
        return c;
    }
}

public static class HardwareProfiles
{
    public const double DefaultRange = 350;

    /// <summary>
    /// Layout used by most 3Dconnexion HID devices according to open-source driver documentation (spacenavd / libspnav):
    /// report 1 = translation (3 x int16 LE), report 2 = rotation (3 x int16 LE), report 3 = button bitmask.
    /// NOT verified against a physical SpaceMouse Compact - run the calibration wizard.
    /// </summary>
    public static HardwareProfile Standard3Report(string name = "3Dconnexion - 3 reports (T, R, buttons)") => new()
    {
        Name = name,
        Reports =
        {
            new ReportLayout { ReportId = 1, Fields = { F(0, 0), F(2, 1), F(4, 2) } },
            new ReportLayout { ReportId = 2, Fields = { F(0, 3), F(2, 4), F(4, 5) } },
            new ReportLayout { ReportId = 3, Buttons = new ButtonField { Offset = 0, LeftBit = 0, RightBit = 1 } },
        },
    };

    /// <summary>Alternative layout: all six axes in one 12 byte report (some newer firmware / wireless variants).</summary>
    public static HardwareProfile Combined6Axis(string name = "3Dconnexion - combined 6-axis report") => new()
    {
        Name = name,
        Reports =
        {
            new ReportLayout { ReportId = 1, Fields = { F(0, 0), F(2, 1), F(4, 2), F(6, 3), F(8, 4), F(10, 5) } },
            new ReportLayout { ReportId = 3, Buttons = new ButtonField { Offset = 0, LeftBit = 0, RightBit = 1 } },
        },
    };

    /// <summary>Wide 16 bit range variant for virtual / high resolution devices.</summary>
    public static HardwareProfile WideRange()
    {
        var p = Standard3Report("Generic - wide range (+-32767)");
        foreach (var a in p.Axes) a.Range = 32767;
        return p;
    }

    public static HardwareProfile Default()
    {
        var p = Standard3Report("SpaceMouse Compact (default, uncalibrated)");
        p.VendorId = 0x256F; p.ProductId = 0xC635;
        foreach (var a in p.Axes) a.Range = DefaultRange;
        return p;
    }

    /// <summary>Guess a layout from observed (reportId, length) pairs. Returns null if nothing recognisable was seen.</summary>
    public static HardwareProfile? Guess(IEnumerable<(byte Id, int Length)> seen)
    {
        var list = seen.ToList();
        if (list.Count == 0) return null;
        var r1 = list.Where(s => s.Id == 1).Select(s => s.Length).DefaultIfEmpty(0).Max();
        var hasRot = list.Any(s => s.Id == 2);
        HardwareProfile p;
        if (r1 >= 13) p = Combined6Axis();
        else if (hasRot || r1 >= 7) p = Standard3Report();
        else return null;
        foreach (var a in p.Axes) a.Range = DefaultRange;
        return p;
    }

    private static FieldDef F(int offset, int slot) => new() { Offset = offset, Slot = slot, Type = FieldType.Int16LE };
}
