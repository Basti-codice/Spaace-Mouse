using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Input;
using SpaceMouse.Core.Persistence;
using SpaceMouse.Core.Profiles;
using Xunit;

namespace SpaceMouse.Core.Tests;

public class PersistenceAndPresetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smtest-" + Guid.NewGuid().ToString("N"));
    public PersistenceAndPresetTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void AllPresets_ReferenceOnlyExistingActionsAndValidKeys()
    {
        var reg = BuiltInActions.Create();
        foreach (var p in Presets.All())
            Assert.Empty(ProfileValidator.Validate(p, reg));
    }

    [Fact]
    public void AllPresets_HaveStableIdsAndFiveKeys()
    {
        var a = Presets.All(); var b = Presets.All();
        Assert.Equal(5, a.Count);
        Assert.Equal(a.Select(p => p.Id), b.Select(p => p.Id));
        Assert.Equal(5, a.Select(p => p.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(Presets.Desktop)]
    [InlineData(Presets.Browser)]
    [InlineData(Presets.Productivity)]
    [InlineData(Presets.WindowManager)]
    public void EveryBinding_OfEveryPreset_ProducesRealOutputWithoutErrors(string key)
    {
        // Drives every axis direction (also with each modifier) and every button; nothing may error and each binding must do something.
        var reg = BuiltInActions.Create();
        var profile = Presets.Create(key);
        foreach (var binding in profile.Axes)
        {
            using var r = new Tests.Rig(null);
            r.Engine.Profiles = new List<Profile> { profile.Clone() };
            r.Engine.SetProfile(r.Engine.Profiles[0]);
            if (binding.Modifier is ButtonModifier.Left or ButtonModifier.Both) r.Press(ButtonId.Left);
            if (binding.Modifier is ButtonModifier.Right or ButtonModifier.Both) r.Press(ButtonId.Right);
            r.Run(0.05);
            r.Platform.Clear();
            foreach (var dir in new[] { 1.0, -1.0 })
            {
                var def = binding.Behavior == AxisBehavior.Continuous ? binding.Action : dir > 0 ? binding.Positive : binding.Negative;
                if (def == null || def.Type == "none") continue;
                if (binding.Action?.Type == "window.move.x" || binding.Action?.Type == "window.move.y")
                    { /* needs grab or target: dry-run foreground window exists */ }
                r.Platform.Clear();
                r.Move(binding.Axis, dir); r.Run(0.6);
                r.Move(binding.Axis, 0); r.Run(0.5);
                Assert.True(r.Log.Length > 0, $"{key}: {binding.Axis} {binding.Modifier} dir {dir} ({def.Type}) produced no output");
                Assert.Empty(r.Errors);
            }
        }
        foreach (var b in profile.Buttons)
        {
            if (b.Action?.Type.StartsWith("quickmenu") == true || b.Action?.Type == "window.grab" || b.Action?.Type == "window.carry") continue; // state-only actions are covered by dedicated tests
            using var r = new Tests.Rig(null);
            r.Engine.Profiles = new List<Profile> { profile.Clone() };
            r.Engine.SetProfile(r.Engine.Profiles[0]);
            r.Press(b.Button); r.Run(0.1); r.Release(b.Button); r.Run(0.1);
            Assert.True(r.Log.Any(l => l.StartsWith("mouse-") || l.StartsWith("key-") || l.StartsWith("window") || l.StartsWith("launch")), $"{key}: button {b.Button} did nothing");
            Assert.Empty(r.Errors);
        }
    }

    [Fact]
    public void AllMenuItemsOfAllPresets_Execute()
    {
        foreach (var key in Presets.Keys)
        {
            var p = Presets.Create(key);
            void Walk(IEnumerable<MenuItem> items)
            {
                foreach (var i in items)
                {
                    if (i.HasChildren) { Walk(i.Children); continue; }
                    using var r = new Tests.Rig(null);
                    r.Engine.Profiles = new List<Profile> { p.Clone() };
                    r.Engine.SetProfile(r.Engine.Profiles[0]);
                    r.Engine.Registry.Create(i.Action, new ActionContext(r.Platform, r.Engine, r.Engine.Registry)).Execute();
                    Assert.True(r.Log.Length > 0, $"{key}/{i.Label} produced nothing");
                }
            }
            foreach (var m in p.QuickMenus) Walk(m.Items);
        }
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var s = new ConfigStore(_dir);
        var set = new AppSettings { Theme = ThemeSetting.Dark, ToggleHotkey = "Ctrl+Alt+M", ActiveProfileId = Guid.NewGuid(), AutoSwitchProfiles = true };
        s.SaveSettings(set);
        var back = new ConfigStore(_dir).LoadSettings();
        Assert.Equal(ThemeSetting.Dark, back.Theme);
        Assert.Equal(set.ActiveProfileId, back.ActiveProfileId);
        Assert.True(back.AutoSwitchProfiles);
        Assert.Equal(AppSettings.CurrentSchema, back.SchemaVersion);
    }

    [Fact]
    public void Profiles_RoundTrip_PreservingBindingsAndMenus()
    {
        var s = new ConfigStore(_dir);
        var list = s.LoadProfiles();
        var custom = Presets.Create(Presets.Browser).Clone();
        custom.PresetKey = null; custom.Id = Guid.NewGuid(); custom.Name = "Mine";
        custom.Axes[0].Tuning.Sensitivity = 1.7;
        list.Add(custom);
        s.SaveProfiles(list);
        var back = new ConfigStore(_dir).LoadProfiles();
        var mine = back.Single(p => p.Name == "Mine");
        Assert.Equal(1.7, mine.Axes[0].Tuning.Sensitivity);
        Assert.Equal(custom.QuickMenus[0].Items.Count, mine.QuickMenus[0].Items.Count);
        Assert.Equal("Sites", mine.QuickMenus[0].Items.Last().Label);
        Assert.Equal(3, mine.QuickMenus[0].Items.Last().Children.Count);
        Assert.Equal(custom.Axes[0].Positive!.Type, mine.Axes[0].Positive!.Type);
    }

    [Fact]
    public void MissingPresets_AreRestoredButEditedOnesKept()
    {
        var s = new ConfigStore(_dir);
        var list = s.LoadProfiles();
        list.First(p => p.PresetKey == Presets.Desktop).Name = "Edited name";
        list.RemoveAll(p => p.PresetKey == Presets.Browser);
        s.SaveProfiles(list);
        var back = new ConfigStore(_dir).LoadProfiles();
        Assert.Equal("Edited name", back.First(p => p.PresetKey == Presets.Desktop).Name);
        Assert.Contains(back, p => p.PresetKey == Presets.Browser);
    }

    [Fact]
    public void CorruptProfileFile_IsQuarantinedAndDefaultsRestored()
    {
        File.WriteAllText(Path.Combine(_dir, "profiles.json"), "{ this is not json");
        var s = new ConfigStore(_dir);
        var list = s.LoadProfiles();
        Assert.Equal(5, list.Count);
        Assert.Single(s.Warnings);
        Assert.Single(Directory.GetFiles(_dir, "profiles.json.corrupt-*"));
    }

    [Fact]
    public void CorruptSettings_FallBackToDefaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "\0\0\0");
        var s = new ConfigStore(_dir);
        Assert.Equal(ThemeSetting.System, s.LoadSettings().Theme);
        Assert.NotEmpty(s.Warnings);
    }

    [Fact]
    public void HardwareProfiles_RoundTripAndDefaultExists()
    {
        var s = new ConfigStore(_dir);
        Assert.Single(s.LoadHardwareProfiles());
        var p = HardwareProfiles.Combined6Axis(); p.Axes[2].Invert = true; p.Axes[2].Range = 512;
        s.SaveHardwareProfiles(new[] { p, HardwareProfiles.Default() });
        var back = new ConfigStore(_dir).LoadHardwareProfiles();
        Assert.Equal(2, back.Count);
        Assert.True(back[0].Axes[2].Invert);
        Assert.Equal(512, back[0].Axes[2].Range);
        Assert.Equal(6, back[0].Reports[0].Fields.Count);
    }

    [Fact]
    public void ExportImport_AssignsNewIdAndDropsPresetKey()
    {
        var reg = BuiltInActions.Create();
        var src = Presets.Create(Presets.Productivity);
        var file = Path.Combine(_dir, "x.json");
        ConfigStore.ExportProfile(src, file);
        var imported = ConfigStore.ImportProfiles(file, reg).Single();
        Assert.NotEqual(src.Id, imported.Id);
        Assert.Null(imported.PresetKey);
        Assert.Equal(src.Axes.Count, imported.Axes.Count);
    }

    [Fact]
    public void Import_RejectsGarbage()
    {
        var reg = BuiltInActions.Create();
        var f = Path.Combine(_dir, "bad.json");
        File.WriteAllText(f, "[1,2,3]");
        Assert.ThrowsAny<Exception>(() => ConfigStore.ImportProfiles(f, reg));
        File.WriteAllText(f, "nope");
        Assert.ThrowsAny<Exception>(() => ConfigStore.ImportProfiles(f, reg));
    }

    [Fact]
    public void Validator_FlagsBrokenActions()
    {
        var reg = BuiltInActions.Create();
        var p = new Profile { Name = "x" };
        p.Buttons.Add(new ButtonBinding { Action = new ActionDef("keys.send", ("keys", "Ctrl+Nope")) });
        p.Buttons.Add(new ButtonBinding { Button = ButtonId.Right, Action = new ActionDef("ghost") });
        Assert.Equal(2, ProfileValidator.Validate(p, reg).Count);
    }

    [Theory]
    [InlineData("Ctrl+Shift+T", new ushort[] { 0x11, 0x10, 0x54 })]
    [InlineData("alt+left", new ushort[] { 0x12, 0x25 })]
    [InlineData("F5", new ushort[] { 0x74 })]
    [InlineData("Win+D", new ushort[] { 0x5B, 0x44 })]
    public void KeyCombo_ParsesChords(string text, ushort[] expected) => Assert.Equal(expected, KeyCombo.ParseChord(text));

    [Fact]
    public void KeyCombo_SequencesAndErrors()
    {
        Assert.Equal(2, KeyCombo.ParseSequence("Ctrl+K, Ctrl+C")!.Count);
        Assert.Null(KeyCombo.ParseChord("Ctrl+Banana"));
        Assert.Null(KeyCombo.ParseSequence(""));
    }

    [Fact]
    public void ResolveAxis_PrefersExactModifierThenFallsBack()
    {
        var p = Presets.Create(Presets.Productivity);
        Assert.Equal(ButtonModifier.None, p.ResolveAxis(Axis.Tx, false, false)!.Modifier);
        Assert.Equal(ButtonModifier.Left, p.ResolveAxis(Axis.Tx, true, false)!.Modifier);
        Assert.Equal(ButtonModifier.None, p.ResolveAxis(Axis.Tx, false, true)!.Modifier);    // no Right binding -> fallback
        Assert.Equal(ButtonModifier.Left, p.ResolveAxis(Axis.Tx, true, true)!.Modifier);     // Both -> Left binding
        Assert.Null(p.ResolveAxis(Axis.Rx, false, false)); // not bound in productivity
    }

    [Fact]
    public void ActionRegistry_DescribesAndCategorises()
    {
        var reg = BuiltInActions.Create();
        Assert.Contains("Mouse", reg.Categories);
        Assert.Contains("Browser", reg.Categories);
        Assert.Equal("Send keyboard shortcut: Ctrl+W", reg.Describe(new ActionDef("keys.send", ("keys", "Ctrl+W"))));
        Assert.Equal("Nothing", reg.Describe(null));
        Assert.True(reg.All.Count() > 60);
    }
}
