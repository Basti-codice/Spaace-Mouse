using System.Runtime.InteropServices;

namespace SpaceMouse.Windows;

internal static class Native
{
    // ---------- window class / message loop ----------
    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int ptX, ptY; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] public static extern nint DefWindowProc(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, nint h, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern nint DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    public static readonly nint HWND_MESSAGE = new(-3);
    public const uint WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_HOTKEY = 0x0312, WM_CLOSE = 0x0010, WM_QUIT = 0x0012, WM_USER = 0x0400;

    // ---------- raw input ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE { public ushort usUsagePage, usUsage; public uint dwFlags; public nint hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST { public nint hDevice; public uint dwType; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER { public uint dwType, dwSize; public nint hDevice, wParam; }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct RID_DEVICE_INFO
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint dwType;
        [FieldOffset(8)] public uint dwVendorId;
        [FieldOffset(12)] public uint dwProductId;
        [FieldOffset(16)] public uint dwVersionNumber;
        [FieldOffset(20)] public ushort usUsagePage;
        [FieldOffset(22)] public ushort usUsage;
    }

    public const uint RIDEV_INPUTSINK = 0x100, RIDEV_DEVNOTIFY = 0x2000, RIDEV_REMOVE = 0x1;
    public const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000b;
    public const uint RIM_TYPEHID = 2;

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devs, uint n, uint size);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[]? list, ref uint n, uint size);
    [DllImport("user32.dll", SetLastError = true)] public static extern uint GetRawInputData(nint h, uint cmd, nint data, ref uint size, uint hdrSize);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern uint GetRawInputDeviceInfo(nint dev, uint cmd, nint data, ref uint size);

    // ---------- hid file access ----------
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateFile(string name, uint access, uint share, nint sec, uint disp, uint flags, nint tmpl);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(nint h, byte[] buf, uint n, out uint read, nint ov);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(nint h, nint ov);
    public const uint GENERIC_READ = 0x80000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    public static readonly nint INVALID_HANDLE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(nint h, out nint data);
    [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] public static extern int HidP_GetCaps(nint data, out HIDP_CAPS caps);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool HidD_GetProductString(nint h, [Out] char[] buf, uint len);
    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool HidD_GetManufacturerString(nint h, [Out] char[] buf, uint len);

    // ---------- SendInput ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT { public uint type; public INPUTUNION u; }
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint MOUSEEVENTF_MOVE = 1, LEFTDOWN = 2, LEFTUP = 4, RIGHTDOWN = 8, RIGHTUP = 0x10, MIDDLEDOWN = 0x20, MIDDLEUP = 0x40,
        WHEEL = 0x800, HWHEEL = 0x1000, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000;
    public const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;

    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int idx);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

    // ---------- windows ----------
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint h);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint h, char[] buf, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint h, char[] buf, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint h);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(nint mon, ref MONITORINFO info);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    public delegate bool MonitorEnumProc(nint mon, nint hdc, ref RECT rect, nint data);
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint h, int attr, out RECT rect, int size);
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint GA_ROOT = 2, MONITOR_DEFAULTTONEAREST = 2;
    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
    public const int SW_MINIMIZE = 6, SW_MAXIMIZE = 3, SW_RESTORE = 9, SW_SHOW = 5;

    // ---------- hotkeys / timers / dpi ----------
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h, int id);
    public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
    [DllImport("kernel32.dll")] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(nint h, uint flags, char[] buf, ref uint size);
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
}
