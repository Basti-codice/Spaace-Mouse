using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;

namespace SpaceMouse.Core.Calibration;

public enum StepKind { Rest, Direction }

public sealed record CalibrationStep(StepKind Kind, Axis Axis, int Sign, string Title, string Hint)
{
    public static IReadOnlyList<CalibrationStep> BuildAll()
    {
        var l = new List<CalibrationStep> { new(StepKind.Rest, Axis.Tx, 0, "Let go of the SpaceMouse", "Keep your hands off the cap so the resting position can be measured.") };
        foreach (var a in AxisInfo.All)
            foreach (var s in new[] { +1, -1 })
                l.Add(new(StepKind.Direction, a, s,
                    s > 0 ? AxisInfo.Positive(a) : AxisInfo.Negative(a),
                    $"{(s > 0 ? AxisInfo.Positive(a) : AxisInfo.Negative(a))} - move the cap as far as it goes, then release."));
        return l;
    }
}

public sealed record StepResult(bool Success, string Message, int Slot = -1, double Deflection = 0);

/// <summary>
/// Hardware calibration logic (UI independent, deterministic). Feed raw slot values, complete steps,
/// then <see cref="Build"/> produces a calibrated <see cref="HardwareProfile"/>.
/// </summary>
public sealed class CalibrationSession
{
    public const double MinDeflectionFraction = 0.12; // of the assumed range
    private readonly HardwareProfile _base;
    private readonly IReadOnlyList<CalibrationStep> _steps = CalibrationStep.BuildAll();
    private readonly double[] _rest = new double[6];
    private readonly double[] _noise = new double[6];
    private readonly List<double[]> _restSamples = new();
    private readonly double[] _peak = new double[6];       // signed extreme deviation during current step
    private readonly double[] _live = new double[6];
    private readonly Dictionary<(Axis, int), (int Slot, double Dev)> _results = new();
    private int _quiet;

    public CalibrationSession(HardwareProfile baseProfile)
    {
        _base = baseProfile.Clone();
        AssumedRange = _base.Axes.Select(a => a.Range).DefaultIfEmpty(HardwareProfiles.DefaultRange).Average();
    }

    public double AssumedRange { get; }
    public IReadOnlyList<CalibrationStep> Steps => _steps;
    public int Index { get; private set; }
    public bool Finished => Index >= _steps.Count;
    public CalibrationStep? Current => Finished ? null : _steps[Index];
    public IReadOnlyList<double> Rest => _rest;
    public IReadOnlyList<double> LiveDeviation => _live;
    public int RestSamples => _restSamples.Count;

    /// <summary>Feed the latest raw slot values (call for every report).</summary>
    public void Feed(IReadOnlyList<double> raw)
    {
        if (Finished) return;
        if (Current!.Kind == StepKind.Rest)
        {
            _restSamples.Add(raw.Take(6).ToArray());
            return;
        }
        var near = true;
        for (var i = 0; i < 6; i++)
        {
            var dev = raw[i] - _rest[i];
            _live[i] = dev;
            if (Math.Abs(dev) > Math.Abs(_peak[i])) _peak[i] = dev;
            if (Math.Abs(dev) > Math.Max(_noise[i] * 2, AssumedRange * 0.05)) near = false;
        }
        _quiet = near ? _quiet + 1 : 0;
    }

    /// <summary>Slot with the strongest deflection in the current step, or -1.</summary>
    public int DetectedSlot
    {
        get
        {
            var best = -1; var m = 0.0;
            for (var i = 0; i < 6; i++) if (Math.Abs(_peak[i]) > m) { m = Math.Abs(_peak[i]); best = i; }
            return m >= AssumedRange * MinDeflectionFraction ? best : -1;
        }
    }

    /// <summary>True when a clear movement was seen and the cap is back at rest - UI may auto-advance.</summary>
    public bool ReadyToAdvance => Current?.Kind == StepKind.Direction && DetectedSlot >= 0 && _quiet >= 4;

    public StepResult Complete()
    {
        if (Finished) return new(false, "Calibration already finished.");
        var step = Current!;
        StepResult res;
        if (step.Kind == StepKind.Rest)
        {
            if (_restSamples.Count < 3) return new(false, "No data received yet. Is the device connected (or the virtual controller running)?");
            for (var s = 0; s < 6; s++)
            {
                _rest[s] = _restSamples.Average(x => x[s]);
                _noise[s] = _restSamples.Max(x => Math.Abs(x[s] - _rest[s]));
            }
            res = new(true, $"Resting position measured from {_restSamples.Count} samples.");
        }
        else
        {
            var slot = DetectedSlot;
            if (slot < 0)
                return new(false, "Movement too small. Push or twist the cap further - all the way to the stop.", Deflection: _peak.Max(Math.Abs));
            // ambiguity check: a second slot nearly as strong means the user moved diagonally
            var sorted = _peak.Select(Math.Abs).OrderByDescending(x => x).ToArray();
            if (sorted.Length > 1 && sorted[1] > sorted[0] * 0.6)
            {
                var r = new StepResult(false, "Several axes moved at once. Move the cap in exactly one direction.", slot, sorted[0]);
                ResetPeak();
                return r;
            }
            _results[(step.Axis, step.Sign)] = (slot, _peak[slot]);
            res = new(true, $"Detected raw channel {slot + 1}.", slot, Math.Abs(_peak[slot]));
        }
        ResetPeak();
        Index++;
        return res;
    }

    /// <summary>Retry the current step (discard its measurements).</summary>
    public void RedoCurrent() { ResetPeak(); _restSamples.Clear(); }

    public void GoBack() { if (Index > 0) { Index--; ResetPeak(); _restSamples.Clear(); } }

    /// <summary>Skip a direction step (user can finish with fewer measurements; missing axes keep defaults).</summary>
    public void Skip() { if (!Finished) { ResetPeak(); Index++; } }

    private void ResetPeak() { Array.Clear(_peak); Array.Clear(_live); _quiet = 0; }

    /// <summary>Problems that make the result unusable (e.g. two axes sharing one channel).</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        var slotOf = new Dictionary<Axis, int>();
        foreach (var a in AxisInfo.All)
        {
            var votes = new[] { +1, -1 }.Where(s => _results.ContainsKey((a, s))).Select(s => _results[(a, s)].Slot).Distinct().ToList();
            if (votes.Count == 0) { problems.Add($"{AxisInfo.Name(a)} was not measured."); continue; }
            if (votes.Count > 1) problems.Add($"{AxisInfo.Name(a)}: the two directions were detected on different channels - repeat that axis.");
            slotOf[a] = votes[0];
        }
        foreach (var g in slotOf.GroupBy(k => k.Value).Where(g => g.Count() > 1))
            problems.Add($"Channel {g.Key + 1} was detected for {string.Join(" and ", g.Select(x => AxisInfo.Name(x.Key)))}. Each movement needs its own channel.");
        return problems;
    }

    public HardwareProfile Build(string? name = null)
    {
        var p = _base.Clone();
        p.Id = Guid.NewGuid();
        if (name != null) p.Name = name;
        foreach (var a in AxisInfo.All)
        {
            var cal = p.Axes[(int)a];
            var meas = new[] { +1, -1 }.Where(s => _results.ContainsKey((a, s))).Select(s => (Sign: s, R: _results[(a, s)])).ToList();
            if (meas.Count == 0) continue;
            var slot = meas[0].R.Slot;
            // invert if the observed deviation sign contradicts the expected direction (majority of the votes, weighted by strength)
            double vote = 0;
            foreach (var m in meas.Where(m => m.R.Slot == slot)) vote += Math.Sign(m.R.Dev) * m.Sign * Math.Abs(m.R.Dev);
            cal.Slot = slot;
            cal.Invert = vote < 0;
            cal.Rest = Math.Round(_rest[slot], 1);
            var range = meas.Where(m => m.R.Slot == slot).Max(m => Math.Abs(m.R.Dev));
            cal.Range = Math.Max(50, Math.Round(range));
            cal.Deadzone = Math.Clamp(Math.Round((_noise[slot] * 1.5) / cal.Range, 3), 0.015, 0.2);
            cal.Gain = 1.0;
        }
        p.Calibrated = Validate().Count == 0;
        return p;
    }
}
