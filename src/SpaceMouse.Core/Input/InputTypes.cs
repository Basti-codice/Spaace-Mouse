namespace SpaceMouse.Core.Input;

/// <summary>The six logical degrees of freedom. Order is fixed and used as array index everywhere.</summary>
public enum Axis { Tx = 0, Ty = 1, Tz = 2, Rx = 3, Ry = 4, Rz = 5 }

public enum ButtonId { Left = 0, Right = 1 }

public enum SourceKind { None, Hardware, Virtual }

/// <summary>Human readable axis metadata (convention: + = right / forward / up / tilt forward / tilt right / twist clockwise).</summary>
public static class AxisInfo
{
    public static readonly Axis[] All = { Axis.Tx, Axis.Ty, Axis.Tz, Axis.Rx, Axis.Ry, Axis.Rz };

    public static string Name(Axis a) => a switch
    {
        Axis.Tx => "Slide left / right",
        Axis.Ty => "Push / pull",
        Axis.Tz => "Lift / press",
        Axis.Rx => "Tilt forward / back",
        Axis.Ry => "Tilt left / right",
        _ => "Twist",
    };

    public static string Short(Axis a) => a switch
    {
        Axis.Tx => "X", Axis.Ty => "Y", Axis.Tz => "Z",
        Axis.Rx => "RX", Axis.Ry => "RY", _ => "RZ",
    };

    public static string Positive(Axis a) => a switch
    {
        Axis.Tx => "Right", Axis.Ty => "Push forward", Axis.Tz => "Lift up",
        Axis.Rx => "Tilt forward", Axis.Ry => "Tilt right", _ => "Twist clockwise",
    };

    public static string Negative(Axis a) => a switch
    {
        Axis.Tx => "Left", Axis.Ty => "Pull back", Axis.Tz => "Press down",
        Axis.Rx => "Tilt back", Axis.Ry => "Tilt left", _ => "Twist counter-clockwise",
    };

    public static bool IsRotation(Axis a) => (int)a >= 3;
}

/// <summary>Immutable normalised snapshot: axes in [-1,1] (after hardware calibration), two buttons.</summary>
public sealed class InputFrame
{
    public static readonly InputFrame Neutral = new(new double[6], false, false);

    private readonly double[] _axes;
    public InputFrame(double[] axes, bool left, bool right)
    {
        if (axes.Length != 6) throw new ArgumentException("Exactly 6 axes required", nameof(axes));
        _axes = (double[])axes.Clone();
        Left = left; Right = right;
    }

    public double this[Axis a] => _axes[(int)a];
    public double Get(int i) => _axes[i];
    public bool Left { get; }
    public bool Right { get; }
    public bool Button(ButtonId b) => b == ButtonId.Left ? Left : Right;
    public double[] ToArray() => (double[])_axes.Clone();
    public bool IsNeutral => !Left && !Right && _axes.All(v => v == 0);
}

/// <summary>A raw HID input report as delivered by the OS (first byte = report id when the device uses report ids).</summary>
public readonly record struct RawReport(byte[] Data, DateTime TimestampUtc)
{
    public byte ReportId => Data.Length > 0 ? Data[0] : (byte)0;
}

public sealed record DeviceDescriptor(
    string Name,
    ushort VendorId,
    ushort ProductId,
    string Path = "",
    string Manufacturer = "",
    int InputReportLength = 0,
    bool IsVirtual = false)
{
    public string UsbId => $"{VendorId:X4}:{ProductId:X4}";
}

/// <summary>Anything that can deliver raw HID reports: the Windows Raw Input reader or the virtual controller.</summary>
public interface IRawReportSource : IDisposable
{
    SourceKind Kind { get; }
    DeviceDescriptor? Device { get; }
    bool IsConnected { get; }
    event Action<RawReport>? ReportReceived;
    event Action<bool, DeviceDescriptor?>? ConnectionChanged;
    void Start();
    void Stop();
}
