using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;

namespace SpaceMouse.Windows;

/// <summary>Enumerates multi-axis controllers (HID usage page 0x01, usage 0x08) through the Raw Input API.</summary>
public static class DeviceScanner
{
    private static readonly Regex VidPid = new(@"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record Found(nint Handle, DeviceDescriptor Descriptor, bool Known);

    public static List<Found> Enumerate()
    {
        var res = new List<Found>();
        uint n = 0;
        var size = (uint)Marshal.SizeOf<Native.RAWINPUTDEVICELIST>();
        if (Native.GetRawInputDeviceList(null, ref n, size) != 0 || n == 0) return res;
        var list = new Native.RAWINPUTDEVICELIST[n];
        if (Native.GetRawInputDeviceList(list, ref n, size) == uint.MaxValue) return res;
        foreach (var d in list.Take((int)n).Where(d => d.dwType == Native.RIM_TYPEHID))
        {
            var f = Describe(d.hDevice);
            if (f != null) res.Add(f);
        }
        // known 3Dconnexion devices first
        return res.OrderByDescending(f => f.Known).ToList();
    }

    public static Found? Describe(nint handle)
    {
        var info = new Native.RID_DEVICE_INFO { cbSize = 32 };
        uint sz = 32;
        var ptr = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (Native.GetRawInputDeviceInfo(handle, Native.RIDI_DEVICEINFO, ptr, ref sz) == uint.MaxValue) return null;
            info = Marshal.PtrToStructure<Native.RID_DEVICE_INFO>(ptr);
        }
        finally { Marshal.FreeHGlobal(ptr); }
        if (info.dwType != Native.RIM_TYPEHID) return null;
        if (info.usUsagePage != KnownDevices.UsagePageGenericDesktop || info.usUsage != KnownDevices.UsageMultiAxis) return null;

        var path = DevicePath(handle);
        var vid = (ushort)info.dwVendorId; var pid = (ushort)info.dwProductId;
        if (vid == 0 && VidPid.Match(path) is { Success: true } m)
        { vid = Convert.ToUInt16(m.Groups[1].Value, 16); pid = Convert.ToUInt16(m.Groups[2].Value, 16); }

        var (product, manufacturer, reportLen) = ReadStrings(path);
        var name = KnownDevices.IsKnown(vid, pid) ? KnownDevices.NameOf(vid, pid)
                 : !string.IsNullOrWhiteSpace(product) ? product : KnownDevices.NameOf(vid, pid);
        return new Found(handle, new DeviceDescriptor(name, vid, pid, path, manufacturer, reportLen), KnownDevices.IsKnown(vid, pid));
    }

    public static string DevicePath(nint handle)
    {
        uint sz = 0;
        Native.GetRawInputDeviceInfo(handle, Native.RIDI_DEVICENAME, 0, ref sz);
        if (sz == 0) return "";
        var ptr = Marshal.AllocHGlobal((int)sz * 2 + 2);
        try
        {
            if (Native.GetRawInputDeviceInfo(handle, Native.RIDI_DEVICENAME, ptr, ref sz) == uint.MaxValue) return "";
            return Marshal.PtrToStringUni(ptr) ?? "";
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static (string product, string manufacturer, int reportLen) ReadStrings(string path)
    {
        if (string.IsNullOrEmpty(path)) return ("", "", 0);
        // access 0 = query only; works even when another driver has the device open.
        var h = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, 0, Native.OPEN_EXISTING, 0, 0);
        if (h == 0 || h == Native.INVALID_HANDLE) return ("", "", 0);
        try
        {
            var b = new char[128];
            var product = Native.HidD_GetProductString(h, b, (uint)b.Length * 2) ? new string(b).TrimEnd('\0') : "";
            b = new char[128];
            var manu = Native.HidD_GetManufacturerString(h, b, (uint)b.Length * 2) ? new string(b).TrimEnd('\0') : "";
            var len = 0;
            if (Native.HidD_GetPreparsedData(h, out var pp))
            {
                try { if (Native.HidP_GetCaps(pp, out var caps) >= 0) len = caps.InputReportByteLength; }
                finally { Native.HidD_FreePreparsedData(pp); }
            }
            return (product, manu, len);
        }
        finally { Native.CloseHandle(h); }
    }
}

/// <summary>
/// Reads the SpaceMouse through the Windows Raw Input API (WM_INPUT, input-sink so it works in the background).
/// This coexists with the official 3Dconnexion driver: the device is never opened exclusively and no driver is touched.
/// </summary>
public sealed class RawInputHidSource : IRawReportSource
{
    private MessageLoopWindow? _win;
    private nint _primary;
    private DeviceDescriptor? _device;
    private bool _connected;
    private readonly object _l = new();

    public SourceKind Kind => SourceKind.Hardware;
    public DeviceDescriptor? Device => _device;
    public bool IsConnected => _connected;
    public long ReportCount { get; private set; }
    public string? LastError { get; private set; }
    public event Action<RawReport>? ReportReceived;
    public event Action<bool, DeviceDescriptor?>? ConnectionChanged;

    public void Start()
    {
        if (_win != null) return;
        try
        {
            _win = new MessageLoopWindow(OnMessage);
            var reg = new[] { new Native.RAWINPUTDEVICE { usUsagePage = KnownDevices.UsagePageGenericDesktop, usUsage = KnownDevices.UsageMultiAxis,
                dwFlags = Native.RIDEV_INPUTSINK | Native.RIDEV_DEVNOTIFY, hwndTarget = _win.Handle } };
            if (!Native.RegisterRawInputDevices(reg, 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>()))
                throw new InvalidOperationException("RegisterRawInputDevices failed (error " + Marshal.GetLastWin32Error() + ").");
            Refresh();
        }
        catch (Exception e)
        {
            LastError = e.Message;
            SetConnected(false, null);
        }
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam)
    {
        if (msg == Native.WM_INPUT_DEVICE_CHANGE) { Refresh(); return true; }
        if (msg != Native.WM_INPUT) return false;
        HandleInput(lParam);
        return false; // let DefWindowProc run: required by the Raw Input API for cleanup
    }

    private void Refresh()
    {
        var devs = DeviceScanner.Enumerate();
        lock (_l)
        {
            var current = devs.FirstOrDefault(d => d.Handle == _primary) ?? devs.FirstOrDefault();
            if (current == null) { _primary = 0; SetConnected(false, null); return; }
            _primary = current.Handle;
            SetConnected(true, current.Descriptor);
        }
    }

    private void SetConnected(bool c, DeviceDescriptor? d)
    {
        var changed = c != _connected || (c && !Equals(d, _device));
        _connected = c; _device = c ? d : null;
        if (changed) ConnectionChanged?.Invoke(c, d);
    }

    private void HandleInput(nint hRawInput)
    {
        uint size = 0;
        var hdrSize = (uint)Marshal.SizeOf<Native.RAWINPUTHEADER>();
        if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, 0, ref size, hdrSize) != 0 || size == 0) return;
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (Native.GetRawInputData(hRawInput, Native.RID_INPUT, buf, ref size, hdrSize) != size) return;
            var hdr = Marshal.PtrToStructure<Native.RAWINPUTHEADER>(buf);
            if (hdr.dwType != Native.RIM_TYPEHID) return;

            lock (_l)
            {
                if (_primary != 0 && hdr.hDevice != _primary)
                {
                    // Another multi-axis device: only take over if we have none (e.g. arrival event was missed).
                    if (_connected) return;
                }
                if (_primary == 0 || !_connected)
                {
                    _primary = hdr.hDevice;
                    var f = DeviceScanner.Describe(hdr.hDevice);
                    SetConnected(true, f?.Descriptor ?? new DeviceDescriptor("Multi-axis controller", 0, 0));
                }
            }

            var p = buf + (int)hdrSize;
            var sizeHid = (uint)Marshal.ReadInt32(p);
            var count = (uint)Marshal.ReadInt32(p, 4);
            if (sizeHid == 0 || sizeHid > 512 || count == 0 || count > 64) return;
            for (var i = 0; i < count; i++)
            {
                var data = new byte[sizeHid];
                Marshal.Copy(p + 8 + (int)(i * sizeHid), data, 0, (int)sizeHid);
                ReportCount++;
                ReportReceived?.Invoke(new RawReport(data, DateTime.UtcNow));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public void Stop()
    {
        if (_win == null) return;
        try
        {
            var rem = new[] { new Native.RAWINPUTDEVICE { usUsagePage = KnownDevices.UsagePageGenericDesktop, usUsage = KnownDevices.UsageMultiAxis, dwFlags = Native.RIDEV_REMOVE } };
            Native.RegisterRawInputDevices(rem, 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
        }
        catch { }
        _win.Dispose(); _win = null;
        SetConnected(false, null);
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Alternative backend: opens the HID device file read-only with shared access and reads input reports directly.
/// Use if Raw Input delivers nothing on a given system. Each open handle has its own report queue, so the official driver is unaffected.
/// </summary>
public sealed class DirectHidSource : IRawReportSource
{
    private Thread? _thread;
    private volatile bool _run;
    private nint _handle;
    private DeviceDescriptor? _device;
    private bool _connected;

    public SourceKind Kind => SourceKind.Hardware;
    public DeviceDescriptor? Device => _device;
    public bool IsConnected => _connected;
    public long ReportCount { get; private set; }
    public string? LastError { get; private set; }
    public event Action<RawReport>? ReportReceived;
    public event Action<bool, DeviceDescriptor?>? ConnectionChanged;

    public void Start()
    {
        if (_run) return;
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SpaceMouse direct HID" };
        _thread.Start();
    }

    private void Loop()
    {
        while (_run)
        {
            try
            {
                var dev = DeviceScanner.Enumerate().FirstOrDefault();
                if (dev == null) { Thread.Sleep(1000); continue; }
                var h = Native.CreateFile(dev.Descriptor.Path, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, 0, Native.OPEN_EXISTING, 0, 0);
                if (h == 0 || h == Native.INVALID_HANDLE)
                {
                    LastError = "Could not open the device (error " + Marshal.GetLastWin32Error() + "). Another program may hold it exclusively.";
                    Thread.Sleep(2000); continue;
                }
                _handle = h;
                var len = Math.Max(dev.Descriptor.InputReportLength, 64);
                _device = dev.Descriptor; _connected = true; LastError = null;
                ConnectionChanged?.Invoke(true, _device);
                var buf = new byte[len];
                while (_run)
                {
                    if (!Native.ReadFile(h, buf, (uint)buf.Length, out var read, 0)) break; // unplugged or cancelled
                    if (read == 0) continue;
                    var data = new byte[read]; Array.Copy(buf, data, read);
                    ReportCount++;
                    ReportReceived?.Invoke(new RawReport(data, DateTime.UtcNow));
                }
                Native.CloseHandle(h); _handle = 0;
                _connected = false; _device = null;
                ConnectionChanged?.Invoke(false, null);
            }
            catch (Exception e) { LastError = e.Message; Thread.Sleep(1000); }
        }
    }

    public void Stop()
    {
        _run = false;
        var h = _handle;
        if (h != 0) { Native.CancelIoEx(h, 0); Native.CloseHandle(h); }
        _thread?.Join(1000); _thread = null;
        if (_connected) { _connected = false; ConnectionChanged?.Invoke(false, null); }
    }

    public void Dispose() => Stop();
}
