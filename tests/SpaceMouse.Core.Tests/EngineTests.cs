using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Engine;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Platform;
using SpaceMouse.Core.Profiles;
using SpaceMouse.Core.Simulator;
using Xunit;

namespace SpaceMouse.Core.Tests;

public class EngineTests
{
    // -------- continuous --------
    [Fact]
    public void FullDeflection_MovesCursorAboutOneThousandPixelsPerSecond()
    {
        using var r = new Rig(Presets.Desktop);
        r.Move(Axis.Tx, 1); r.Run(1.0);
        Assert.InRange(r.CursorSum(true), 900, 1100);
        Assert.Equal(0, r.CursorSum(false));
    }

    [Fact]
    public void ForwardPush_MovesCursorUp()
    {
        using var r = new Rig(Presets.Desktop);
        r.Move(Axis.Ty, 1); r.Run(0.5);
        Assert.True(r.CursorSum(false) < -400);
    }

    [Fact]
    public void InsideDeadzone_NothingHappens()
    {
        using var r = new Rig(Presets.Desktop);
        r.Move(Axis.Tx, 0.05); r.Run(1.0);   // preset deadzone 0.08
        Assert.Equal(0, r.Count("cursor "));
    }

    [Fact]
    public void Inversion_ReversesDirection()
    {
        using var r = new Rig(Presets.Desktop);
        r.Engine.ActiveProfile!.Axes.First(a => a.Axis == Axis.Tx).Tuning.Invert = true;
        r.Engine.Reload();
        r.Move(Axis.Tx, 1); r.Run(0.5);
        Assert.True(r.CursorSum(true) < -400);
    }

    [Fact]
    public void Sensitivity_And_Speed_ScaleOutput()
    {
        int Measure(double speed, double sens)
        {
            using var r = new Rig(Presets.Desktop);
            var t = r.Engine.ActiveProfile!.Axes.First(a => a.Axis == Axis.Tx).Tuning;
            t.Speed = speed; t.Sensitivity = sens; r.Engine.Reload();
            r.Move(Axis.Tx, 1); r.Run(1);
            return r.CursorSum(true);
        }
        var baseline = Measure(1, 1);
        Assert.InRange(Measure(2, 1), baseline * 1.9, baseline * 2.1);
        Assert.InRange(Measure(1, 0.5), baseline * 0.45, baseline * 0.55);
    }

    [Fact]
    public void Acceleration_MakesSmallDeflectionsGentler()
    {
        int Measure(double accel)
        {
            using var r = new Rig(Presets.Desktop);
            var t = r.Engine.ActiveProfile!.Axes.First(a => a.Axis == Axis.Tx).Tuning;
            t.Acceleration = accel; t.Deadzone = 0; r.Engine.Reload();
            r.Move(Axis.Tx, 0.4); r.Run(1);
            return r.CursorSum(true);
        }
        Assert.True(Measure(2.0) < Measure(1.0) * 0.6);
    }

    [Fact]
    public void StrongMovementMultiplier_AppliesAboveThreshold()
    {
        using var r = new Rig(Presets.Desktop);
        var t = r.Engine.ActiveProfile!.Axes.First(a => a.Axis == Axis.Tx).Tuning;
        t.StrongThreshold = 0.9; t.StrongMultiplier = 3; r.Engine.Reload();
        r.Move(Axis.Tx, 1); r.Run(0.5);
        Assert.True(r.CursorSum(true) > 1200);
    }

    [Fact]
    public void Scrolling_UsesWheelUnits()
    {
        using var r = new Rig(Presets.Desktop);
        r.Move(Axis.Rz, 1); r.Run(1);
        var total = r.Log.Where(l => l.StartsWith("wheel ")).Sum(l => int.Parse(l.Substring(6)));
        Assert.InRange(total, 1000, 1300);
    }

    [Fact]
    public void SubPixelMotion_IsAccumulatedNotLost()
    {
        using var r = new Rig(Presets.Desktop);
        var t = r.Engine.ActiveProfile!.Axes.First(a => a.Axis == Axis.Tx).Tuning;
        t.Deadzone = 0; t.Acceleration = 1; t.Speed = 0.01; r.Engine.Reload();   // 10 px/s
        r.Move(Axis.Tx, 1); r.Run(1);
        Assert.InRange(r.CursorSum(true), 8, 11);
    }

    // -------- buttons --------
    [Fact]
    public void HoldMode_PressesAndReleasesMouseButton()
    {
        using var r = new Rig(Presets.Desktop);
        r.Press(ButtonId.Left); r.Run(0.05);
        Assert.Equal(1, r.Count("mouse-down Left"));
        Assert.Equal(0, r.Count("mouse-up Left"));
        r.Release(ButtonId.Left); r.Run(0.05);
        Assert.Equal(1, r.Count("mouse-up Left"));
    }

    [Fact]
    public void TapMode_FiresOnceOnRelease()
    {
        using var r = new Rig(Presets.Browser);
        r.Press(ButtonId.Left); r.Run(0.1);
        Assert.Equal(0, r.Count("mouse-down"));
        r.Release(ButtonId.Left); r.Run(0.02);
        Assert.Equal(1, r.Count("mouse-down Left"));
        Assert.Equal(1, r.Count("mouse-up Left"));
    }

    [Fact]
    public void LongPress_FiresAlternativeAction()
    {
        using var r = new Rig(Presets.Productivity);
        r.Press(ButtonId.Left); r.Run(0.7); r.Release(ButtonId.Left); r.Run(0.02);
        Assert.Equal(1, r.Count("mouse-down Right"));
        Assert.Equal(0, r.Count("mouse-down Left"));
    }

    [Fact]
    public void ButtonUsedAsModifier_DoesNotAlsoFireItsTapAction()
    {
        using var r = new Rig(Presets.Productivity);
        r.Press(ButtonId.Left); r.Run(0.05);
        r.Move(Axis.Tx, 1); r.Run(0.1);               // Left + X = next virtual desktop
        r.Move(Axis.Tx, 0); r.Run(0.05);
        r.Release(ButtonId.Left); r.Run(0.05);
        Assert.Equal(0, r.Count("mouse-down"));
    }

    // -------- discrete --------
    [Fact]
    public void Discrete_FiresOncePerGesture_NotOncePerTick()
    {
        using var r = new Rig(Presets.Browser);
        r.Move(Axis.Rz, 1); r.Run(1.0);   // held for a second (no repeat)
        Assert.Equal(1, r.Count("key-down 0x11"));   // exactly one Ctrl+Tab
        Assert.Equal(1, r.Executed.Count(e => e.Contains("Next tab")));
        r.Move(Axis.Rz, 0); r.Run(0.1);
    }

    [Fact]
    public void Discrete_NextTabSendsCtrlTab_PreviousSendsCtrlShiftTab()
    {
        using var r = new Rig(Presets.Browser);
        r.Move(Axis.Rz, 1); r.Run(0.1); r.Move(Axis.Rz, 0); r.Run(0.5);
        Assert.Contains("key-down 0x09", r.Log);
        Assert.DoesNotContain("key-down 0x10", r.Log);
        r.Platform.Clear();
        r.Move(Axis.Rz, -1); r.Run(0.1); r.Move(Axis.Rz, 0); r.Run(0.5);
        var l = r.Log.ToList();
        Assert.True(l.IndexOf("key-down 0x11") < l.IndexOf("key-down 0x10") && l.IndexOf("key-down 0x10") < l.IndexOf("key-down 0x09"));
    }

    [Fact]
    public void Discrete_RetriggersAfterReturningToRestAndCooldown()
    {
        using var r = new Rig(Presets.Browser);
        for (var i = 0; i < 3; i++) { r.Move(Axis.Rz, 1); r.Run(0.1); r.Move(Axis.Rz, 0); r.Run(0.5); }
        Assert.Equal(3, r.Executed.Count(e => e.Contains("Next tab")));
    }

    [Fact]
    public void Discrete_CooldownBlocksJitter()
    {
        using var r = new Rig(Presets.Browser);
        for (var i = 0; i < 5; i++) { r.Move(Axis.Rz, 1); r.Run(0.03); r.Move(Axis.Rz, 0); r.Run(0.03); }
        Assert.Equal(1, r.Executed.Count(e => e.Contains("Next tab")));
    }

    [Fact]
    public void Discrete_BelowThreshold_DoesNotFire()
    {
        using var r = new Rig(Presets.Browser);
        r.Move(Axis.Rz, 0.4); r.Run(1);
        Assert.Empty(r.Executed);
    }

    [Fact]
    public void Discrete_RepeatWhileHeld()
    {
        using var r = new Rig(Presets.Browser);
        var t = r.Engine.ActiveProfile!.Axes.First(a => a.Axis == Axis.Rz).Tuning;
        t.Repeat = true; t.RepeatMs = 500; t.CooldownMs = 100; r.Engine.Reload(); r.Run(0.03);
        r.Move(Axis.Rz, 1); r.Run(2.1);
        Assert.InRange(r.Executed.Count(e => e.Contains("Next tab")), 4, 6);
    }

    // -------- combinations --------
    [Fact]
    public void Combination_LeftHeldPlusTranslation_UsesModifierBinding()
    {
        using var r = new Rig(Presets.Productivity);
        r.Move(Axis.Tx, 1); r.Run(0.2);
        Assert.True(r.CursorSum(true) > 100);        // unmodified -> cursor
        r.Move(Axis.Tx, 0); r.Run(0.1);
        r.Platform.Clear(); r.Executed.Clear();
        r.Press(ButtonId.Left); r.Run(0.05);
        r.Move(Axis.Tx, 1); r.Run(0.2);
        Assert.Contains(r.Executed, e => e.Contains("Next virtual desktop"));
        Assert.Equal(0, r.Count("cursor "));
    }

    [Fact]
    public void Combination_HeldAxisDoesNotRetriggerWhenModifierPressed()
    {
        using var r = new Rig(Presets.Productivity);
        r.Move(Axis.Tx, 1); r.Run(0.1);
        r.Executed.Clear();
        r.Press(ButtonId.Left); r.Run(0.3);          // axis still deflected -> no accidental trigger
        Assert.DoesNotContain(r.Executed, e => e.Contains("virtual desktop"));
    }

    [Fact]
    public void WindowManager_GrabAndMoveWindow()
    {
        using var r = new Rig(Presets.WindowManager);
        r.Press(ButtonId.Left); r.Run(0.05);
        r.Move(Axis.Tx, 1); r.Run(0.5);
        Assert.InRange(r.Log.Where(l => l.StartsWith("window-move")).Sum(l => int.Parse(l.Split(' ')[1].Split(',')[0])), 400, 650);
        Assert.Equal(0, r.Count("cursor "));
    }

    // -------- modes --------
    [Fact]
    public void StandardMode_InjectsNothing()
    {
        using var r = new Rig(Presets.Desktop, custom: false);
        r.Move(Axis.Tx, 1); r.Press(ButtonId.Left); r.Run(1);
        Assert.DoesNotContain(r.Log, l => !l.StartsWith("release-all"));
    }

    [Fact]
    public void LeavingCustomMode_ReleasesHeldInput()
    {
        using var r = new Rig(Presets.Desktop);
        r.Press(ButtonId.Left); r.Run(0.05);
        r.Engine.SetMode(EngineMode.Standard);
        Assert.Contains("mouse-up Left", r.Log);
        Assert.Contains("release-all", r.Log);
        r.Run(0.1); r.Release(ButtonId.Left); r.Run(0.1);
        Assert.Equal(1, r.Count("mouse-up Left"));      // no duplicate release afterwards
    }

    [Fact]
    public void SwitchingProfile_ReleasesHeldInput()
    {
        using var r = new Rig(Presets.Desktop);
        r.Press(ButtonId.Left); r.Run(0.05);
        r.Engine.SwitchProfile("Browser");
        Assert.Contains("mouse-up Left", r.Log);
        Assert.Equal("Browser", r.Engine.ActiveProfile!.Name);
    }

    [Fact]
    public void Dispose_ReleasesEverything()
    {
        var r = new Rig(Presets.Desktop);
        r.Press(ButtonId.Left); r.Run(0.05);
        r.Dispose();
        Assert.Contains("mouse-up Left", r.Log);
    }

    [Fact]
    public void EnablingCustomMode_WithButtonAlreadyHeld_DoesNotTriggerStalePress()
    {
        using var r = new Rig(Presets.Desktop, custom: false);
        r.Press(ButtonId.Left); r.Run(0.1);
        r.Engine.SetMode(EngineMode.Custom); r.Run(0.1);
        Assert.Equal(0, r.Count("mouse-down"));
    }

    [Fact]
    public void DeviceUnplugged_ReleasesHeldButtons()
    {
        using var r = new Rig(Presets.Desktop);
        r.Press(ButtonId.Left); r.Move(Axis.Tx, 1); r.Run(0.1);
        r.Device.SetConnected(false);
        r.Run(0.1);
        Assert.Contains("mouse-up Left", r.Log);
        var n = r.Count("cursor ");
        r.Run(0.5);
        Assert.Equal(n, r.Count("cursor "));         // no more movement after unplug
        r.Device.SetConnected(true); r.Run(0.1);
        Assert.Equal(SourceKind.Virtual, r.Hub.ActiveKind);
    }

    [Fact]
    public void ProfileSwitchActions_CycleProfiles()
    {
        using var r = new Rig(Presets.Desktop);
        var names = new List<string>();
        r.Engine.ProfileChanged += p => names.Add(p!.Name);
        r.Engine.NextProfile(); r.Engine.NextProfile(); r.Engine.PrevProfile();
        Assert.Equal(new[] { "Browser", "Productivity", "Browser" }, names);
    }

    [Fact]
    public void ToggleAction_SwitchesBetweenStandardAndCustom()
    {
        using var r = new Rig(Presets.Desktop);
        r.Engine.ToggleActive();
        Assert.Equal(EngineMode.Standard, r.Engine.Mode);
        r.Engine.ToggleActive();
        Assert.Equal(EngineMode.Custom, r.Engine.Mode);
    }

    // -------- auto switch --------
    [Fact]
    public void AutoSwitch_UsesRulesAndFallsBackToManualProfile()
    {
        using var r = new Rig(Presets.Desktop);
        r.Engine.AutoSwitchEnabled = true;
        r.Engine.Profiles.First(p => p.PresetKey == Presets.Browser).AppRules.Add(new AppRule { ProcessName = "chrome.exe" });
        r.Engine.ApplyForeground("Chrome");
        Assert.Equal("Browser", r.Engine.ActiveProfile!.Name);
        r.Engine.ApplyForeground("notepad");
        Assert.Equal("Standard Desktop", r.Engine.ActiveProfile!.Name);
    }

    [Fact]
    public void AutoSwitch_DisabledDoesNothing()
    {
        using var r = new Rig(Presets.Desktop);
        r.Engine.Profiles.First(p => p.PresetKey == Presets.Browser).AppRules.Add(new AppRule { ProcessName = "chrome" });
        r.Engine.ApplyForeground("chrome");
        Assert.Equal("Standard Desktop", r.Engine.ActiveProfile!.Name);
    }

    // -------- robustness --------
    [Fact]
    public void FailingAction_IsReportedAndEngineKeepsRunning()
    {
        using var r = new Rig(Presets.Custom);
        var p = r.Engine.ActiveProfile!;
        p.Buttons.Add(new ButtonBinding { Button = ButtonId.Left, Mode = ButtonMode.Press, Action = new ActionDef("program.launch") });
        p.Buttons.Add(new ButtonBinding { Button = ButtonId.Right, Mode = ButtonMode.Press, Action = new ActionDef("keys.send", ("keys", "Ctrl+Q")) });
        r.Engine.Reload();
        r.Press(ButtonId.Left); r.Run(0.05);
        Assert.Single(r.Errors);
        r.Press(ButtonId.Right); r.Run(0.05);
        Assert.Contains("key-down 0x51", r.Log);
    }

    [Fact]
    public void InvalidKeyString_IsReportedNotThrown()
    {
        using var r = new Rig(Presets.Custom);
        r.Engine.ActiveProfile!.Buttons.Add(new ButtonBinding { Button = ButtonId.Left, Mode = ButtonMode.Press, Action = new ActionDef("keys.send", ("keys", "Ctrl+Banana")) });
        r.Engine.Reload();
        r.Press(ButtonId.Left); r.Run(0.05);
        Assert.NotEmpty(r.Errors);
        Assert.Empty(r.Log.Where(l => l.StartsWith("key-")));
    }

    [Fact]
    public void UnknownActionType_IsHarmless()
    {
        using var r = new Rig(Presets.Custom);
        r.Engine.ActiveProfile!.Axes.Add(new AxisBinding { Axis = Axis.Tx, Action = new ActionDef("does.not.exist") });
        r.Engine.Reload();
        r.Move(Axis.Tx, 1); r.Run(0.2);
        Assert.DoesNotContain(r.Log, l => !l.StartsWith("release-all"));
    }

    [Fact]
    public void KeyChord_ReleasesInReverseOrder_EvenWhenInjectionFails()
    {
        var sim = new ThrowingSim();
        Assert.Throws<InvalidOperationException>(() => KeyCombo.Send(sim, new ushort[] { 0x11, 0x57 }));
        Assert.Equal(new[] { "down 17", "up 17" }, sim.Calls);
    }

    private sealed class ThrowingSim : IInputSimulator
    {
        public List<string> Calls = new();
        public void KeyDown(ushort vk) { if (vk == 0x57) throw new InvalidOperationException(); Calls.Add($"down {vk}"); }
        public void KeyUp(ushort vk) => Calls.Add($"up {vk}");
        public void MoveCursorBy(int dx, int dy) { }
        public void SetCursor(int x, int y) { }
        public (int X, int Y) GetCursor() => (0, 0);
        public void MouseDown(MouseButton b) { }
        public void MouseUp(MouseButton b) { }
        public void Wheel(int d) { }
        public void HWheel(int d) { }
        public void ReleaseAll() { }
    }

    [Fact]
    public void Macro_RunsStepsInOrder()
    {
        using var r = new Rig(Presets.Custom);
        var macro = new ActionDef("macro.sequence");
        macro.Steps.Add(new ActionDef("keys.send", ("keys", "Ctrl+A")));
        macro.Steps.Add(new ActionDef("macro.wait", ("ms", "30")));
        macro.Steps.Add(new ActionDef("keys.send", ("keys", "Ctrl+C")));
        r.Engine.ActiveProfile!.Buttons.Add(new ButtonBinding { Button = ButtonId.Left, Mode = ButtonMode.Press, Action = macro });
        r.Engine.Reload();
        r.Press(ButtonId.Left); r.Run(0.05);
        SpinWait.SpinUntil(() => r.Count("key-down 0x43") > 0, 2000);
        var log = r.Log.ToList();
        Assert.True(log.IndexOf("key-down 0x41") < log.IndexOf("key-down 0x43"));
    }

    // -------- quick menu --------
    [Fact]
    public void QuickMenu_HoldRightSelectsByDirectionAndConfirmsOnRelease()
    {
        using var r = new Rig(Presets.Browser);
        QuickMenuView? opened = null; MenuItem? closedWith = null;
        r.Engine.Menu.Opened += v => opened = v;
        r.Engine.Menu.Closed += i => closedWith = i;
        r.Press(ButtonId.Right); r.Run(0.05);
        Assert.NotNull(opened);
        Assert.True(r.Engine.Menu.IsOpen);
        // 8 items, sector 0 = up (New tab). Push forward = up.
        r.Move(Axis.Ty, 1); r.Run(0.05);
        Assert.Equal(0, r.Engine.Menu.Selected);
        // right = 90 deg = 2 sectors -> item index 2 (Reload)
        r.Move(Axis.Ty, 0); r.Move(Axis.Tx, 1); r.Run(0.05);
        Assert.Equal(2, r.Engine.Menu.Selected);
        r.Move(Axis.Tx, 0); r.Move(Axis.Ty, 1); r.Move(Axis.Tx, 0); r.Run(0.02);
        r.Release(ButtonId.Right); r.Run(0.05);
        Assert.False(r.Engine.Menu.IsOpen);
        Assert.Equal("New tab", closedWith!.Label);
        Assert.Contains("key-down 0x54", r.Log);           // Ctrl+T
    }

    [Fact]
    public void QuickMenu_CursorStaysStillWhileMenuOpen()
    {
        using var r = new Rig(Presets.Desktop);
        r.Engine.ActiveProfile!.Buttons.Clear();
        r.Engine.ActiveProfile.Buttons.Add(new ButtonBinding { Button = ButtonId.Right, Mode = ButtonMode.Hold, Action = new ActionDef("quickmenu.hold") });
        r.Engine.Reload();
        r.Press(ButtonId.Right); r.Run(0.05);
        r.Move(Axis.Tx, 1); r.Run(0.5);
        Assert.Equal(0, r.Count("cursor "));
    }

    [Fact]
    public void QuickMenu_ReleaseInCenter_Cancels()
    {
        using var r = new Rig(Presets.Browser);
        r.Press(ButtonId.Right); r.Run(0.05);
        r.Release(ButtonId.Right); r.Run(0.05);
        Assert.False(r.Engine.Menu.IsOpen);
        Assert.DoesNotContain(r.Log, l => l.StartsWith("key-"));
    }

    [Fact]
    public void QuickMenu_Submenu_EnterAndGoBack()
    {
        using var r = new Rig(Presets.Browser);
        r.Press(ButtonId.Right); r.Run(0.05);
        var sites = r.Engine.Menu.Items.ToList().FindIndex(i => i.Label == "Sites");
        Assert.True(sites > 0);
        r.Engine.Menu.Select(sites);
        r.Release(ButtonId.Right); r.Run(0.05);                 // confirm on a folder -> opens submenu and stays open (sticky)
        Assert.True(r.Engine.Menu.IsOpen);
        Assert.Equal("Back", r.Engine.Menu.Items[0].Label);
        Assert.Equal(4, r.Engine.Menu.Items.Count);
        r.Engine.Menu.Select(0); r.Engine.ConfirmQuickMenu();   // back
        Assert.Equal(8, r.Engine.Menu.Items.Count);
        r.Engine.CancelQuickMenu();
        Assert.False(r.Engine.Menu.IsOpen);
    }

    [Fact]
    public void QuickMenu_Submenu_LeafLaunchesUrl()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.OpenQuickMenu(null, holdMode: false);
        r.Engine.Menu.Select(r.Engine.Menu.Items.ToList().FindIndex(i => i.Label == "Sites"));
        r.Engine.ConfirmQuickMenu();
        r.Engine.Menu.Select(1);                                // Google
        r.Engine.ConfirmQuickMenu();
        Assert.Contains(r.Log, l => l.StartsWith("launch https://www.google.com"));
    }

    [Fact]
    public void QuickMenu_StickyMode_LeftConfirmsRightCancels()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.OpenQuickMenu(null, holdMode: false);
        r.Engine.Menu.Select(0);
        r.Press(ButtonId.Left); r.Run(0.05); r.Release(ButtonId.Left); r.Run(0.05);
        Assert.False(r.Engine.Menu.IsOpen);
        Assert.Contains("key-down 0x54", r.Log);
        Assert.Equal(0, r.Count("mouse-down"));                 // the click was swallowed by the menu
        r.Engine.OpenQuickMenu(null, false);
        r.Press(ButtonId.Right); r.Run(0.05); r.Release(ButtonId.Right); r.Run(0.05);
        Assert.False(r.Engine.Menu.IsOpen);
    }

    [Fact]
    public void QuickMenu_RotationMode_StepsThroughItems()
    {
        using var r = new Rig(Presets.Browser);
        var m = r.Engine.ActiveProfile!.QuickMenus[0];
        m.SelectMode = MenuSelectMode.Rotation;
        r.Engine.Reload();
        r.Engine.OpenQuickMenu(null, false);
        r.Move(Axis.Rz, 1); r.Run(0.05);
        Assert.Equal(0, r.Engine.Menu.Selected);
        r.Run(0.5);
        var forward = r.Engine.Menu.Selected;
        Assert.InRange(forward, 2, 5);
        r.Move(Axis.Rz, -1); r.Run(0.3);
        Assert.True(r.Engine.Menu.Selected < forward);
    }

    [Fact]
    public void QuickMenu_PushDownConfirms()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.ActiveProfile!.QuickMenus[0].ConfirmWithPush = true;
        r.Engine.Reload();
        r.Engine.OpenQuickMenu(null, false);
        r.Move(Axis.Ty, 1); r.Run(0.05);
        r.Move(Axis.Tz, -1); r.Run(0.05);
        Assert.False(r.Engine.Menu.IsOpen);
        Assert.Contains("key-down 0x54", r.Log);
    }

    [Fact]
    public void QuickMenu_ClosesWhenLeavingCustomMode()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.OpenQuickMenu(null, false);
        r.Engine.SetMode(EngineMode.Standard);
        Assert.False(r.Engine.Menu.IsOpen);
    }

    [Fact]
    public void QuickMenu_AutoClosesAfterTimeout()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.OpenQuickMenu(null, false);
        r.Run(QuickMenuController.MaxOpenSeconds + 1, dt: 0.1);
        Assert.False(r.Engine.Menu.IsOpen);
    }

    [Fact]
    public void QuickMenu_EmptyOrMissing_ReportsError()
    {
        using var r = new Rig(Presets.Custom);
        r.Engine.OpenQuickMenu(null, false);
        Assert.NotEmpty(r.Errors);
        Assert.False(r.Engine.Menu.IsOpen);
    }

    // -------- works with every hardware variant --------
    [Theory]
    [InlineData(VirtualLayout.ThreeReports, 350)]
    [InlineData(VirtualLayout.Combined6, 350)]
    [InlineData(VirtualLayout.WideRange, 32767)]
    public void SameBehaviour_ForAllHardwareVariants(VirtualLayout l, double range)
    {
        using var r = new Rig(Presets.Desktop, l, range);
        r.Move(Axis.Tx, 1); r.Run(1);
        Assert.InRange(r.CursorSum(true), 900, 1100);
    }
}

public class UxFeatureTests
{
    [Fact]
    public void WindowCarry_PicksUpOnFirstPress_MovesWithCursorAxes_DropsOnSecond()
    {
        using var r = new Rig(Presets.WindowManager);
        r.Press(ButtonId.Right); r.Run(0.05); r.Release(ButtonId.Right); r.Run(0.05);   // tap = pick up
        Assert.Contains(r.Executed, e => e.Contains("Pick up"));
        r.Platform.Clear();
        r.Move(Axis.Tx, 1); r.Run(0.5);
        Assert.True(r.Count("window-move") > 10);
        Assert.Equal(0, r.Count("cursor "));            // cursor stays, window moves
        r.Move(Axis.Tx, 0); r.Run(0.05);
        r.Press(ButtonId.Right); r.Run(0.05); r.Release(ButtonId.Right); r.Run(0.05);   // drop
        r.Platform.Clear();
        r.Move(Axis.Tx, 1); r.Run(0.3);
        Assert.Equal(0, r.Count("window-move"));
        Assert.True(r.Count("cursor ") > 5);            // cursor moves again
    }

    [Fact]
    public void WindowCarry_IsDroppedWhenControlIsSwitchedOff()
    {
        using var r = new Rig(Presets.WindowManager);
        r.Press(ButtonId.Right); r.Run(0.05); r.Release(ButtonId.Right); r.Run(0.05);
        r.Engine.SetMode(EngineMode.Standard); r.Engine.SetMode(EngineMode.Custom); r.Run(0.05);
        r.Platform.Clear();
        r.Move(Axis.Tx, 1); r.Run(0.3);
        Assert.Equal(0, r.Count("window-move"));
    }

    [Fact]
    public void QuickMenu_Magnetism_DoesNotFlickerAtSectorBorder()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.OpenQuickMenu(null, false);
        r.Move(Axis.Ty, 1); r.Run(0.03);
        Assert.Equal(0, r.Engine.Menu.Selected);
        // 8 items -> sector 45 deg; border at 22.5 deg. 24 deg is inside the 15 % hysteresis band.
        var a = 24 * Math.PI / 180;
        r.Device.SetAxes(Math.Sin(a), Math.Cos(a), 0, 0, 0, 0); r.Run(0.03);
        Assert.Equal(0, r.Engine.Menu.Selected);
        a = 35 * Math.PI / 180;
        r.Device.SetAxes(Math.Sin(a), Math.Cos(a), 0, 0, 0, 0); r.Run(0.03);
        Assert.Equal(1, r.Engine.Menu.Selected);
    }

    [Fact]
    public void QuickMenu_PointerFollowsDeflection()
    {
        using var r = new Rig(Presets.Browser);
        r.Engine.OpenQuickMenu(null, false);
        r.Move(Axis.Tx, 0.8); r.Run(0.03);
        Assert.Equal(0.8, r.Engine.Menu.Pointer.X, 1);
    }
}
