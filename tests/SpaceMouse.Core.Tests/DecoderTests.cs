using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Simulator;
using Xunit;

namespace SpaceMouse.Core.Tests;

public class DecoderTests
{
    private static HardwareProfile Exact(HardwareProfile? p = null) { p ??= HardwareProfiles.Default(); foreach (var a in p.Axes) a.Deadzone = 0; return p; }

    private static byte[] Report(byte id, params short[] v)
    {
        var d = new byte[1 + v.Length * 2]; d[0] = id;
        for (var i = 0; i < v.Length; i++) { d[1 + i * 2] = (byte)(v[i] & 0xFF); d[2 + i * 2] = (byte)((v[i] >> 8) & 0xFF); }
        return d;
    }

    [Fact]
    public void ThreeReportLayout_DecodesTranslationRotationAndButtons()
    {
        var dec = new ReportDecoder(Exact());
        Assert.True(dec.Decode(Report(1, 175, -350, 0)));
        Assert.True(dec.Decode(Report(2, 0, 350, -175)));
        Assert.True(dec.Decode(new byte[] { 3, 0b10 }));
        var f = dec.CurrentFrame();
        Assert.Equal(0.5, f[Axis.Tx], 3);
        Assert.Equal(-1.0, f[Axis.Ty], 3);
        Assert.Equal(1.0, f[Axis.Ry], 3);
        Assert.Equal(-0.5, f[Axis.Rz], 3);
        Assert.False(f.Left); Assert.True(f.Right);
    }

    [Fact]
    public void CombinedLayout_DecodesAllSixInOneReport()
    {
        var p = Exact(HardwareProfiles.Combined6Axis());
        foreach (var a in p.Axes) a.Range = 350;
        var dec = new ReportDecoder(p);
        dec.Decode(Report(1, 350, 0, -350, 175, 0, -175));
        var f = dec.CurrentFrame();
        Assert.Equal(new[] { 1.0, 0, -1, 0.5, 0, -0.5 }, f.ToArray().Select(v => Math.Round(v, 3)).ToArray());
    }

    [Fact]
    public void UnknownOrTruncatedReports_AreIgnoredWithoutChangingState()
    {
        var dec = new ReportDecoder(HardwareProfiles.Default());
        dec.Decode(Report(1, 100, 100, 100));
        Assert.False(dec.Decode(new byte[] { 9, 1, 2, 3 }));        // unknown id
        Assert.False(dec.Decode(new byte[] { 1, 5, 0 }));           // truncated
        Assert.False(dec.Decode(Array.Empty<byte>()));
        Assert.Equal(100, dec.RawSlots[0]);
    }

    [Fact]
    public void Normalisation_ClampsAtRange()
    {
        var dec = new ReportDecoder(HardwareProfiles.Default());
        dec.Decode(Report(1, 32000, -32000, 0));
        var f = dec.CurrentFrame();
        Assert.Equal(1.0, f[Axis.Tx]); Assert.Equal(-1.0, f[Axis.Ty]);
    }

    [Fact]
    public void HardwareDeadzone_RemovesNoiseAndRescales()
    {
        var p = HardwareProfiles.Default();
        foreach (var a in p.Axes) a.Deadzone = 0.1;
        var dec = new ReportDecoder(p);
        dec.Decode(Report(1, 20, 0, 0));      // 0.057 < 0.1 -> 0
        Assert.Equal(0, dec.CurrentFrame()[Axis.Tx]);
        dec.Decode(Report(1, 350, 0, 0));
        Assert.Equal(1.0, dec.CurrentFrame()[Axis.Tx], 6);
        dec.Decode(Report(1, 192, 0, 0));     // (0.5486-0.1)/0.9 = 0.4984
        Assert.Equal(0.498, dec.CurrentFrame()[Axis.Tx], 2);
    }

    [Fact]
    public void AxisRemapAndInvert_AreApplied()
    {
        var p = HardwareProfiles.Default();
        p.Axes[(int)Axis.Tx].Slot = 2; p.Axes[(int)Axis.Tx].Invert = true;   // logical X from raw channel 3, inverted
        var dec = new ReportDecoder(p);
        dec.Decode(Report(1, 0, 0, 350));
        Assert.Equal(-1.0, dec.CurrentFrame()[Axis.Tx]);
    }

    [Fact]
    public void RestOffset_IsSubtracted()
    {
        var p = HardwareProfiles.Default();
        p.Axes[0].Rest = 10;
        var dec = new ReportDecoder(p);
        dec.Decode(Report(1, 10, 0, 0));
        Assert.Equal(0, dec.CurrentFrame()[Axis.Tx]);
    }

    [Fact]
    public void Gain_ScalesAndClamps()
    {
        var p = Exact();
        p.Axes[0].Gain = 2;
        var dec = new ReportDecoder(p);
        dec.Decode(Report(1, 100, 0, 0));
        Assert.Equal(2 * 100 / 350.0, dec.CurrentFrame()[Axis.Tx], 3);
        dec.Decode(Report(1, 300, 0, 0));
        Assert.Equal(1.0, dec.CurrentFrame()[Axis.Tx]);
    }

    [Fact]
    public void ReportDescriptorGuess_PicksLayoutFromObservedReports()
    {
        Assert.Contains("combined", HardwareProfiles.Guess(new[] { ((byte)1, 13) })!.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, HardwareProfiles.Guess(new[] { ((byte)1, 7), ((byte)2, 7), ((byte)3, 5) })!.Reports.Count);
        Assert.Null(HardwareProfiles.Guess(Array.Empty<(byte, int)>()));
    }

    [Theory]
    [InlineData(VirtualLayout.ThreeReports, 350)]
    [InlineData(VirtualLayout.Combined6, 350)]
    [InlineData(VirtualLayout.WideRange, 32767)]
    public void VirtualDevice_RoundTripsThroughHub_ForEveryLayout(VirtualLayout layout, double range)
    {
        var dev = new VirtualSpaceMouse(layout, range);
        using var hub = new InputHub(dev.MatchingProfile());
        hub.Attach(dev);
        dev.SetAxes(0.5, -0.25, 1, -1, 0.75, 0);
        dev.SetButton(ButtonId.Left, true);
        var f = hub.Current;
        Assert.Equal(0.5, f[Axis.Tx], 2); Assert.Equal(-0.25, f[Axis.Ty], 2); Assert.Equal(1, f[Axis.Tz], 2);
        Assert.Equal(-1, f[Axis.Rx], 2); Assert.Equal(0.75, f[Axis.Ry], 2); Assert.Equal(0, f[Axis.Rz], 2);
        Assert.True(f.Left); Assert.False(f.Right);
    }

    [Fact]
    public void Hub_Disconnect_PublishesNeutralFrameAndReportsNoSource()
    {
        var dev = new VirtualSpaceMouse();
        using var hub = new InputHub(dev.MatchingProfile());
        var frames = new List<InputFrame>();
        hub.FrameReceived += frames.Add;
        hub.Attach(dev);
        dev.SetAxis(Axis.Tx, 1); dev.SetButton(ButtonId.Left, true);
        Assert.Equal(SourceKind.Virtual, hub.ActiveKind);
        dev.SetConnected(false);
        Assert.Equal(SourceKind.None, hub.ActiveKind);
        Assert.True(hub.Current.IsNeutral);
        Assert.True(frames.Last().IsNeutral);
        dev.SetConnected(true);
        Assert.Equal(SourceKind.Virtual, hub.ActiveKind);
    }

    [Fact]
    public void Hub_DetachedHasNoSource_NeverClaimsHardware()
    {
        using var hub = new InputHub(HardwareProfiles.Default());
        Assert.Equal(SourceKind.None, hub.ActiveKind);
        Assert.False(hub.IsConnected);
    }

    [Fact]
    public void KnownDevices_IdentifiesCompact()
    {
        Assert.True(KnownDevices.IsCompact(0x256F, 0xC635));
        Assert.Equal("SpaceMouse Compact", KnownDevices.NameOf(0x256F, 0xC635));
        Assert.Equal("Multi-axis controller", KnownDevices.NameOf(0x1234, 0x5678));
    }
}
