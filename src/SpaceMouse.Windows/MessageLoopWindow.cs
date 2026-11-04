using System.Runtime.InteropServices;

namespace SpaceMouse.Windows;

/// <summary>
/// Message-only window running on its own thread with its own message loop. Raw Input and hotkeys are delivered here,
/// so UI stalls can never delay or lose SpaceMouse input.
/// </summary>
public sealed class MessageLoopWindow : IDisposable
{
    private static int _classCounter;
    private readonly Native.WndProc _proc; // keep alive
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private const uint WM_RUN = Native.WM_USER + 1;
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> _work = new();
    private nint _hwnd;
    private string? _error;

    public MessageLoopWindow(Func<uint, nint, nint, bool> handler)
    {
        _proc = (h, m, w, l) =>
        {
            try
            {
                if (m == WM_RUN) { while (_work.TryDequeue(out var a)) { try { a(); } catch { } } return 0; }
                if (handler(m, w, l)) return 0;
            }
            catch { /* never throw into the OS */ }
            if (m == 0x0002) Native.PostQuitMessage(0); // WM_DESTROY
            return Native.DefWindowProc(h, m, w, l);
        };
        _thread = new Thread(Run) { IsBackground = true, Name = "SpaceMouse message loop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(5000) || _hwnd == 0) throw new InvalidOperationException(_error ?? "Message window could not be created.");
    }

    public nint Handle => _hwnd;

    private void Run()
    {
        try
        {
            var cls = "SpaceMouseStudioMsg" + Interlocked.Increment(ref _classCounter);
            var wc = new Native.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = Native.GetModuleHandle(null),
                lpszClassName = cls,
            };
            if (Native.RegisterClassEx(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
            _hwnd = Native.CreateWindowEx(0, cls, "SpaceMouseStudio", 0, 0, 0, 0, 0, Native.HWND_MESSAGE, 0, wc.hInstance, 0);
            if (_hwnd == 0) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        }
        catch (Exception e) { _error = e.Message; _ready.Set(); return; }
        _ready.Set();
        while (Native.GetMessage(out var msg, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessage(ref msg);
        }
    }

    /// <summary>Run code on the loop thread (RegisterHotKey is thread-affine) and wait for it.</summary>
    public void Invoke(Action a)
    {
        if (_hwnd == 0) return;
        using var done = new ManualResetEventSlim();
        Exception? err = null;
        _work.Enqueue(() => { try { a(); } catch (Exception e) { err = e; } finally { done.Set(); } });
        Native.PostMessage(_hwnd, WM_RUN, 0, 0);
        if (!done.Wait(3000)) throw new TimeoutException("Message loop did not respond.");
        if (err != null) throw err;
    }

    public void Dispose()
    {
        if (_hwnd != 0) { Native.PostMessage(_hwnd, Native.WM_CLOSE, 0, 0); }
        _thread.Join(1000);
    }
}
