using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Engine;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Platform;
using SpaceMouse.Core.Profiles;
using SpaceMouse.Core.Simulator;

namespace SpaceMouse.Core.Tests;

/// <summary>Full pipeline without hardware: virtual device -> raw HID bytes -> InputHub -> engine -> dry-run platform.</summary>
public sealed class Rig : IDisposable
{
    public VirtualSpaceMouse Device;
    public InputHub Hub;
    public DryRunPlatform Platform = new();
    public SpaceMouseEngine Engine;
    public List<string> Errors = new();
    public List<string> Executed = new();

    public Rig(string? preset = null, VirtualLayout layout = VirtualLayout.ThreeReports, double range = 350, bool custom = true)
    {
        Device = new VirtualSpaceMouse(layout, range);
        Hub = new InputHub(Device.MatchingProfile());
        Engine = new SpaceMouseEngine(BuiltInActions.Create(), Platform);
        Engine.Profiles = Presets.All();
        Hub.FrameReceived += Engine.SubmitFrame;
        Engine.ErrorOccurred += Errors.Add;
        Engine.ActionExecuted += Executed.Add;
        Hub.Attach(Device);
        if (preset != null) Engine.SetProfile(Engine.Profiles.First(p => p.PresetKey == preset));
        if (custom) Engine.SetMode(EngineMode.Custom);
        Run(0.03);              // let bindings settle (axes armed, edges synced)
        Platform.Clear();
    }

    /// <summary>Advance simulated time in 8 ms ticks.</summary>
    public void Run(double seconds, double dt = 0.008)
    {
        for (var t = 0.0; t < seconds - 1e-9; t += dt) Engine.Tick(dt);
    }

    public void Move(Axis a, double v) => Device.SetAxis(a, v);
    public void Press(ButtonId b) => Device.SetButton(b, true);
    public void Release(ButtonId b) => Device.SetButton(b, false);
    public string[] Log => Platform.Snapshot();
    public int Count(string startsWith) => Log.Count(l => l.StartsWith(startsWith));

    public int CursorSum(bool x)
    {
        var sum = 0;
        foreach (var l in Log.Where(l => l.StartsWith("cursor ")))
        {
            var parts = l.Substring(7).Split(',');
            sum += int.Parse(parts[x ? 0 : 1]);
        }
        return sum;
    }

    public void Dispose() { Engine.Dispose(); Hub.Dispose(); }
}
