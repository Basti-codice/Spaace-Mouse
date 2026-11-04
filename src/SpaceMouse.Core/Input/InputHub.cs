using SpaceMouse.Core.Hid;

namespace SpaceMouse.Core.Input;

/// <summary>
/// Central input abstraction layer. Every raw report - from real hardware or the virtual controller -
/// passes through here and leaves as a normalised <see cref="InputFrame"/>. Nothing downstream knows the source.
/// </summary>
public sealed class InputHub : IDisposable
{
    private readonly object _lock = new();
    private readonly ReportDecoder _decoder;
    private IRawReportSource? _source;
    private readonly Dictionary<(byte, int), int> _seen = new();

    public InputHub(HardwareProfile profile) { _decoder = new ReportDecoder(profile); }

    public event Action<InputFrame>? FrameReceived;
    public event Action<RawReport>? RawReceived;
    public event Action? SourceChanged;

    public InputFrame Current { get; private set; } = InputFrame.Neutral;
    public SourceKind ActiveKind => _source is { IsConnected: true } s ? s.Kind : SourceKind.None;
    public SourceKind AttachedKind => _source?.Kind ?? SourceKind.None;
    public DeviceDescriptor? Device => _source?.Device;
    public bool IsConnected => _source?.IsConnected ?? false;
    public HardwareProfile Profile => _decoder.Profile;
    public IReadOnlyList<double> RawSlots { get { lock (_lock) return _decoder.RawSlots.ToArray(); } }
    public IReadOnlyList<(byte Id, int Length)> SeenReports { get { lock (_lock) return _seen.Keys.Select(k => (k.Item1, k.Item2)).ToList(); } }

    public void SetProfile(HardwareProfile profile)
    {
        lock (_lock) { _decoder.Profile = profile; Current = _decoder.CurrentFrame(); }
        FrameReceived?.Invoke(Current);
    }

    public void Attach(IRawReportSource? source)
    {
        IRawReportSource? old;
        lock (_lock)
        {
            old = _source;
            _source = source;
            _decoder.Reset();
            _seen.Clear();
            Current = InputFrame.Neutral;
        }
        if (old != null)
        {
            old.ReportReceived -= OnReport;
            old.ConnectionChanged -= OnConn;
            old.Stop();
        }
        if (source != null)
        {
            source.ReportReceived += OnReport;
            source.ConnectionChanged += OnConn;
            source.Start();
        }
        FrameReceived?.Invoke(InputFrame.Neutral);
        SourceChanged?.Invoke();
    }

    private void OnConn(bool connected, DeviceDescriptor? d)
    {
        if (!connected)
        {
            // Device vanished: publish a neutral frame so nothing stays "held".
            lock (_lock) { _decoder.Reset(); Current = InputFrame.Neutral; }
            FrameReceived?.Invoke(InputFrame.Neutral);
        }
        SourceChanged?.Invoke();
    }

    private void OnReport(RawReport r)
    {
        InputFrame frame;
        lock (_lock)
        {
            if (r.Data.Length > 0) _seen[(r.Data[0], r.Data.Length)] = _seen.GetValueOrDefault((r.Data[0], r.Data.Length)) + 1;
            if (!_decoder.Decode(r.Data)) { RawReceived?.Invoke(r); return; }
            frame = _decoder.CurrentFrame();
            Current = frame;
        }
        RawReceived?.Invoke(r);
        FrameReceived?.Invoke(frame);
    }

    public void Dispose() => Attach(null);
}
