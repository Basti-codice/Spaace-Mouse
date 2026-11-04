using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;

namespace SpaceMouse.Core.Simulator;

public enum VirtualLayout { ThreeReports, Combined6, WideRange }

/// <summary>
/// Virtual SpaceMouse. Emits genuine HID byte reports (same format as a real device) so the whole pipeline -
/// decoder, calibration, normalisation, engine - is exercised identically for hardware and simulation.
/// </summary>
public sealed class VirtualSpaceMouse : IRawReportSource
{
    private readonly object _l = new();
    private readonly double[] _axes = new double[6]; // -1..1
    private bool _left, _right, _connected = true, _started;
    private VirtualLayout _layout;
    private double _range;

    public VirtualSpaceMouse(VirtualLayout layout = VirtualLayout.ThreeReports, double range = 350)
    {
        _layout = layout; _range = range;
    }

    public SourceKind Kind => SourceKind.Virtual;
    public DeviceDescriptor? Device => new("Virtual SpaceMouse", 0xFFFF, 0x0001, "virtual://spacemouse", "Space Mouse Studio", 0, true);
    public bool IsConnected => _connected && _started;
    public event Action<RawReport>? ReportReceived;
    public event Action<bool, DeviceDescriptor?>? ConnectionChanged;

    public VirtualLayout Layout => _layout;
    public double Range => _range;
    public double[] Axes { get { lock (_l) return (double[])_axes.Clone(); } }
    public bool Left => _left;
    public bool Right => _right;

    /// <summary>The hardware profile a decoder needs to read what this virtual device sends.</summary>
    public HardwareProfile MatchingProfile()
    {
        var p = _layout switch
        {
            VirtualLayout.Combined6 => HardwareProfiles.Combined6Axis("Virtual - combined report"),
            VirtualLayout.WideRange => HardwareProfiles.WideRange(),
            _ => HardwareProfiles.Standard3Report("Virtual - 3 reports"),
        };
        p.VendorId = 0xFFFF; p.ProductId = 1;
        foreach (var a in p.Axes) { a.Range = _range; a.Deadzone = 0; }
        return p;
    }

    public void Configure(VirtualLayout layout, double range)
    {
        lock (_l) { _layout = layout; _range = range; }
        EmitAll();
    }

    public void Start()
    {
        _started = true;
        ConnectionChanged?.Invoke(_connected, Device);
        EmitAll();
    }

    public void Stop()
    {
        _started = false;
        ConnectionChanged?.Invoke(false, Device);
    }

    public void SetAxis(Axis a, double value)
    {
        lock (_l) _axes[(int)a] = Math.Clamp(value, -1, 1);
        Emit(a is Axis.Tx or Axis.Ty or Axis.Tz ? 1 : 2);
    }

    public void SetAxes(double tx, double ty, double tz, double rx, double ry, double rz)
    {
        lock (_l) { _axes[0] = Math.Clamp(tx, -1, 1); _axes[1] = Math.Clamp(ty, -1, 1); _axes[2] = Math.Clamp(tz, -1, 1);
                    _axes[3] = Math.Clamp(rx, -1, 1); _axes[4] = Math.Clamp(ry, -1, 1); _axes[5] = Math.Clamp(rz, -1, 1); }
        EmitAll();
    }

    public void SetButton(ButtonId b, bool down)
    {
        lock (_l) { if (b == ButtonId.Left) _left = down; else _right = down; }
        Emit(3);
    }

    public void Center() { lock (_l) Array.Clear(_axes); EmitAll(); }

    /// <summary>Simulate unplugging / re-plugging.</summary>
    public void SetConnected(bool connected)
    {
        if (_connected == connected) return;
        _connected = connected;
        if (!connected) { lock (_l) { Array.Clear(_axes); _left = _right = false; } }
        ConnectionChanged?.Invoke(connected && _started, Device);
        if (connected) EmitAll();
    }

    private void EmitAll() { Emit(1); Emit(2); Emit(3); }

    private void Emit(int which)
    {
        if (!IsConnected) return;
        byte[]? data;
        lock (_l) data = Build(which);
        if (data != null) ReportReceived?.Invoke(new RawReport(data, DateTime.UtcNow));
    }

    private byte[]? Build(int id)
    {
        short S(double v) => (short)Math.Round(Math.Clamp(v, -1, 1) * _range);
        if (_layout == VirtualLayout.Combined6)
        {
            if (id is 1 or 2)
            {
                if (id == 2) return null; // combined layout has a single motion report
                var d = new byte[13]; d[0] = 1;
                for (var i = 0; i < 6; i++) Put(d, 1 + i * 2, S(_axes[i]));
                return d;
            }
        }
        else if (id == 1) { var d = new byte[7]; d[0] = 1; for (var i = 0; i < 3; i++) Put(d, 1 + i * 2, S(_axes[i])); return d; }
        else if (id == 2) { var d = new byte[7]; d[0] = 2; for (var i = 0; i < 3; i++) Put(d, 1 + i * 2, S(_axes[3 + i])); return d; }
        if (id == 3) return new byte[] { 3, (byte)((_left ? 1 : 0) | (_right ? 2 : 0)), 0, 0, 0 };
        return null;
    }

    private static void Put(byte[] d, int o, short v) { d[o] = (byte)(v & 0xFF); d[o + 1] = (byte)((v >> 8) & 0xFF); }

    public void Dispose() => Stop();
}
