using SpaceMouse.Core.Profiles;

namespace SpaceMouse.Core.Engine;

/// <summary>
/// The user-sensitivity response of one axis binding (software dead zone, curve, gain, boost).
/// Shared by the engine and the UI live preview so both always agree.
/// </summary>
public static class AxisMath
{
    /// <summary>Dead-zone-rescaled magnitude (0..1) of a raw deflection, honouring the invert option.</summary>
    public static double Deadzoned(AxisTuning t, double raw, out double sign)
    {
        if (t.Invert) raw = -raw;
        sign = raw < 0 ? -1 : 1;
        var mag = Math.Abs(raw);
        var dz = Math.Clamp(t.Deadzone, 0, 0.95);
        return mag <= dz ? 0 : (mag - dz) / (1 - dz);
    }

    /// <summary>Signed output for continuous actions (value=1 means "base speed of the action" x Speed).</summary>
    public static double Continuous(AxisTuning t, double raw)
    {
        var a = Deadzoned(t, raw, out var sign);
        if (a <= 0) return 0;
        a = Math.Pow(a, Math.Max(0.1, t.Acceleration));
        if (t.WeakZone > 0 && a < t.WeakZone) a *= t.WeakDamping;
        a *= t.Sensitivity;
        if (Math.Abs(raw) >= t.StrongThreshold) a *= t.StrongMultiplier;
        return sign * a * t.Speed;
    }

    /// <summary>Signed 0..1 deflection level compared against the trigger threshold for discrete actions.</summary>
    public static double Discrete(AxisTuning t, double raw)
    {
        var a = Deadzoned(t, raw, out var sign);
        return sign * Math.Min(1.0, a * Math.Max(0.01, t.Sensitivity));
    }
}
