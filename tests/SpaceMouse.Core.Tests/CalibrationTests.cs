using SpaceMouse.Core.Calibration;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using Xunit;

namespace SpaceMouse.Core.Tests;

public class CalibrationTests
{
    /// <summary>Simulates a device whose channel order and signs differ from the default.</summary>
    private static HardwareProfile Run(int[] slotOfAxis, bool[] invert, double restOffset = 4, double amp = 300, Action<CalibrationSession>? tweak = null)
    {
        var s = new CalibrationSession(HardwareProfiles.Default());
        var rest = Enumerable.Range(0, 6).Select(i => restOffset + i).ToArray();
        for (var i = 0; i < 10; i++) s.Feed(rest);
        Assert.True(s.Complete().Success);
        foreach (var step in s.Steps.Skip(1))
        {
            var ai = (int)step.Axis;
            var raw = (double[])rest.Clone();
            for (var k = 1; k <= 3; k++)
            {
                raw[slotOfAxis[ai]] = rest[slotOfAxis[ai]] + step.Sign * (invert[ai] ? -1 : 1) * amp * k / 3;
                s.Feed(raw);
            }
            for (var k = 0; k < 6; k++) s.Feed(rest);
            Assert.True(s.ReadyToAdvance);
            var r = s.Complete();
            Assert.True(r.Success, r.Message);
        }
        tweak?.Invoke(s);
        Assert.True(s.Finished);
        return s.Build("Test");
    }

    [Fact]
    public void DetectsScrambledChannelOrderAndInversion()
    {
        var slots = new[] { 2, 0, 1, 5, 3, 4 };
        var inv = new[] { false, true, true, false, false, true };
        var p = Run(slots, inv);
        for (var a = 0; a < 6; a++)
        {
            Assert.Equal(slots[a], p.Axes[a].Slot);
            Assert.Equal(inv[a], p.Axes[a].Invert);
            Assert.Equal(300, p.Axes[a].Range, 0);
        }
        Assert.True(p.Calibrated);
    }

    [Fact]
    public void CalibratedProfile_NormalisesSimulatedDeviceCorrectly()
    {
        var slots = new[] { 1, 2, 0, 4, 5, 3 };
        var inv = new[] { true, false, false, true, false, false };
        var p = Run(slots, inv, restOffset: 8);
        var dec = new ReportDecoder(p);
        // build a report 1+2 with logical X pushed positive (raw slot 1, inverted device => negative raw)
        var d1 = new byte[7]; d1[0] = 1;
        short raw = (short)(p.Axes[0].Rest - 300);   // device sends negative for logical +X
        d1[3] = (byte)(raw & 0xFF); d1[4] = (byte)((raw >> 8) & 0xFF); // slot 1 lives at offset 2
        dec.Decode(d1);
        Assert.True(dec.CurrentFrame()[Axis.Tx] > 0.9);
    }

    [Fact]
    public void RestNoise_BecomesHardwareDeadzone()
    {
        var s = new CalibrationSession(HardwareProfiles.Default());
        var rnd = new Random(1);
        for (var i = 0; i < 100; i++) s.Feed(Enumerable.Range(0, 6).Select(_ => rnd.NextDouble() * 6 - 3).ToArray());
        s.Complete();
        Assert.All(s.Rest, r => Assert.InRange(r, -1, 1));
    }

    [Fact]
    public void TooSmallMovement_IsRejectedWithHelpfulMessage()
    {
        var s = new CalibrationSession(HardwareProfiles.Default());
        for (var i = 0; i < 5; i++) s.Feed(new double[6]);
        s.Complete();
        s.Feed(new double[] { 10, 0, 0, 0, 0, 0 });
        var r = s.Complete();
        Assert.False(r.Success);
        Assert.Contains("further", r.Message);
        Assert.Equal(1, s.Index); // stays on the same step
    }

    [Fact]
    public void DiagonalMovement_IsRejected()
    {
        var s = new CalibrationSession(HardwareProfiles.Default());
        for (var i = 0; i < 5; i++) s.Feed(new double[6]);
        s.Complete();
        s.Feed(new double[] { 300, 280, 0, 0, 0, 0 });
        Assert.False(s.Complete().Success);
    }

    [Fact]
    public void RestStepWithoutData_Fails()
    {
        var s = new CalibrationSession(HardwareProfiles.Default());
        Assert.False(s.Complete().Success);
    }

    [Fact]
    public void DuplicateChannels_AreReportedAndProfileNotMarkedCalibrated()
    {
        var p = Run(new[] { 0, 0, 1, 2, 3, 4 }, new bool[6]);
        Assert.False(p.Calibrated);
    }

    [Fact]
    public void SkippedAxes_ReportProblem()
    {
        var s = new CalibrationSession(HardwareProfiles.Default());
        for (var i = 0; i < 5; i++) s.Feed(new double[6]);
        s.Complete();
        while (!s.Finished) s.Skip();
        Assert.NotEmpty(s.Validate());
        Assert.False(s.Build().Calibrated);
    }
}
