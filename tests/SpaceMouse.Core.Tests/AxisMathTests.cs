using SpaceMouse.Core.Engine;
using SpaceMouse.Core.Profiles;
using Xunit;

namespace SpaceMouse.Core.Tests;

public class AxisMathTests
{
    private static AxisTuning T(Action<AxisTuning>? f = null) { var t = new AxisTuning { Deadzone = 0.1, Acceleration = 1, Sensitivity = 1, Speed = 1 }; f?.Invoke(t); return t; }

    [Fact] public void Deadzone_ZeroInside_RescalesOutside()
    {
        Assert.Equal(0, AxisMath.Continuous(T(), 0.09));
        Assert.Equal(1.0, AxisMath.Continuous(T(), 1.0), 6);
        Assert.Equal(0.5, AxisMath.Continuous(T(), 0.55), 6);   // (0.55-0.1)/0.9
    }

    [Fact] public void NegativeInput_GivesNegativeOutput() => Assert.Equal(-1.0, AxisMath.Continuous(T(), -1.0), 6);

    [Fact] public void Invert_FlipsSign() => Assert.Equal(-1.0, AxisMath.Continuous(T(t => t.Invert = true), 1.0), 6);

    [Fact] public void Curve_Exponent_ReducesSmallValues()
    {
        var lin = AxisMath.Continuous(T(), 0.5);
        var curved = AxisMath.Continuous(T(t => t.Acceleration = 2), 0.5);
        Assert.True(curved < lin);
        Assert.Equal(1.0, AxisMath.Continuous(T(t => t.Acceleration = 2), 1.0), 6);   // full deflection unchanged
    }

    [Fact] public void Sensitivity_And_Speed_Multiply()
        => Assert.Equal(3.0, AxisMath.Continuous(T(t => { t.Sensitivity = 1.5; t.Speed = 2; }), 1.0), 6);

    [Fact] public void StrongBoost_OnlyAboveThreshold()
    {
        var t = T(x => { x.StrongThreshold = 0.8; x.StrongMultiplier = 2; });
        Assert.Equal(0.5, AxisMath.Continuous(t, 0.55), 6);
        Assert.Equal(2.0, AxisMath.Continuous(t, 1.0), 6);
    }

    [Fact] public void WeakDamping_AppliesBelowWeakZone()
    {
        var t = T(x => { x.WeakZone = 0.3; x.WeakDamping = 0.5; });
        var raw = 0.1 + 0.9 * 0.2;     // processed a = 0.2 (< 0.3)
        Assert.Equal(0.1, AxisMath.Continuous(t, raw), 6);
    }

    [Fact] public void Discrete_LevelIsClampedAndSigned()
    {
        Assert.Equal(1.0, AxisMath.Discrete(T(t => t.Sensitivity = 5), 0.9));
        Assert.Equal(-1.0, AxisMath.Discrete(T(t => t.Sensitivity = 5), -0.9));
        Assert.Equal(0, AxisMath.Discrete(T(), 0.05));
    }
}
