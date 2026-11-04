using System.Diagnostics;

namespace SpaceMouse.Core.Engine;

/// <summary>Fixed-rate loop (~125 Hz) that drives <see cref="SpaceMouseEngine.Tick"/>. dt is measured, never assumed.</summary>
public sealed class EngineRunner : IDisposable
{
    private readonly SpaceMouseEngine _engine;
    private readonly int _periodMs;
    private Thread? _thread;
    private volatile bool _run;

    public EngineRunner(SpaceMouseEngine engine, int periodMs = 8) { _engine = engine; _periodMs = periodMs; }

    public void Start()
    {
        if (_run) return;
        _run = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "SpaceMouse engine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        var last = sw.Elapsed.TotalSeconds;
        while (_run)
        {
            Thread.Sleep(_periodMs);
            var now = sw.Elapsed.TotalSeconds;
            var dt = Math.Min(0.05, now - last); // clamp after stalls so the cursor never jumps
            last = now;
            try { _engine.Tick(dt); } catch { /* the loop must survive anything */ }
        }
    }

    public void Stop() { _run = false; _thread?.Join(500); _thread = null; }
    public void Dispose() => Stop();
}
