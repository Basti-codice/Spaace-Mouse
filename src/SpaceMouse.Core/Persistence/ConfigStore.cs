using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SpaceMouse.Core.Actions;
using SpaceMouse.Core.Hid;
using SpaceMouse.Core.Profiles;

namespace SpaceMouse.Core.Persistence;

public enum ThemeSetting { System, Light, Dark }

public sealed class AppSettings
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public ThemeSetting Theme { get; set; } = ThemeSetting.System;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ActivateOnStart { get; set; }
    public bool AutoSwitchProfiles { get; set; }
    public bool ShowToasts { get; set; } = true;
    /// <summary>Toggle hotkey (RegisterHotKey) e.g. "Ctrl+Alt+S". Empty = disabled.</summary>
    public string ToggleHotkey { get; set; } = "Ctrl+Alt+S";
    public Guid? ActiveProfileId { get; set; }
    public Guid? ActiveHardwareProfileId { get; set; }
    /// <summary>true = custom mode was active when the app closed.</summary>
    public bool LastModeCustom { get; set; }
    public bool FirstRunDone { get; set; }
    public bool VirtualControllerSendsToSystem { get; set; }
    /// <summary>"RawInput" (default, recommended) or "DirectHid" (fallback backend).</summary>
    public string InputMethod { get; set; } = "RawInput";
    public int ConfigVersionNote { get; set; }
}

/// <summary>Everything user-created, persisted in one versioned JSON file per concern under %APPDATA%\SpaceMouseStudio.</summary>
public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _dir;
    public ConfigStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "backup"));
    }

    public string Directory_ => _dir;
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceMouseStudio");

    /// <summary>Messages about recovered/corrupt files, shown to the user once.</summary>
    public List<string> Warnings { get; } = new();

    private string Path_(string name) => Path.Combine(_dir, name);

    // ---------- atomic file IO ----------
    private void WriteAtomic(string file, string content)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, content);
        if (File.Exists(file))
        {
            File.Replace(tmp, file, null);
        }
        else File.Move(tmp, file);
    }

    private T? ReadOrQuarantine<T>(string name, Func<JsonNode, JsonNode> migrate) where T : class
    {
        var file = Path_(name);
        if (!File.Exists(file)) return null;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(file)) ?? throw new JsonException("empty");
            node = migrate(node);
            return node.Deserialize<T>(Json) ?? throw new JsonException("null");
        }
        catch (Exception e)
        {
            var bad = file + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            try { File.Move(file, bad); } catch { /* ignore */ }
            Warnings.Add($"'{name}' could not be read ({e.Message}). A copy was kept as {System.IO.Path.GetFileName(bad)} and defaults were restored.");
            return null;
        }
    }

    // ---------- migration hooks (schema version -> upgrade) ----------
    public static JsonNode MigrateSettings(JsonNode n) => n; // v1 is current; add steps here: if (v < 2) {...}
    public static JsonNode MigrateProfiles(JsonNode n) => n;
    public static JsonNode MigrateHardware(JsonNode n) => n;

    // ---------- settings ----------
    public AppSettings LoadSettings() => ReadOrQuarantine<AppSettings>("settings.json", MigrateSettings) ?? new AppSettings();
    public void SaveSettings(AppSettings s) => WriteAtomic(Path_("settings.json"), JsonSerializer.Serialize(s, Json));

    // ---------- profiles ----------
    private sealed class ProfileFile { public int SchemaVersion { get; set; } = Profile.CurrentSchema; public List<Profile> Profiles { get; set; } = new(); }

    /// <summary>Loads profiles; makes sure all presets exist (missing presets are restored, edited presets are kept).</summary>
    public List<Profile> LoadProfiles()
    {
        var f = ReadOrQuarantine<ProfileFile>("profiles.json", MigrateProfiles);
        var list = f?.Profiles.Where(p => p != null).ToList() ?? new List<Profile>();
        foreach (var key in Presets.Keys)
            if (!list.Any(p => p.PresetKey == key)) list.Add(Presets.Create(key));
        // order: presets first (stable), then user profiles
        return list.OrderBy(p => p.PresetKey == null ? 1 : 0).ThenBy(p => Array.IndexOf(Presets.Keys, p.PresetKey)).ToList();
    }

    public void SaveProfiles(IEnumerable<Profile> profiles)
        => WriteAtomic(Path_("profiles.json"), JsonSerializer.Serialize(new ProfileFile { Profiles = profiles.ToList() }, Json));

    // ---------- hardware profiles ----------
    private sealed class HardwareFile { public int SchemaVersion { get; set; } = 1; public List<HardwareProfile> Profiles { get; set; } = new(); }

    public List<HardwareProfile> LoadHardwareProfiles()
    {
        var f = ReadOrQuarantine<HardwareFile>("hardware.json", MigrateHardware);
        var list = f?.Profiles ?? new List<HardwareProfile>();
        if (list.Count == 0) list.Add(HardwareProfiles.Default());
        return list;
    }

    public void SaveHardwareProfiles(IEnumerable<HardwareProfile> p)
        => WriteAtomic(Path_("hardware.json"), JsonSerializer.Serialize(new HardwareFile { Profiles = p.ToList() }, Json));

    // ---------- import / export ----------
    public static void ExportProfile(Profile p, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(new ProfileFile { Profiles = { p } }, Json));

    /// <summary>Imports and validates a profile file. New ids are assigned so imports never overwrite existing profiles.</summary>
    public static List<Profile> ImportProfiles(string path, ActionRegistry registry)
    {
        var node = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("File is empty.");
        node = MigrateProfiles(node);
        List<Profile>? res;
        if (node is JsonObject o && o.ContainsKey("Profiles") || node is JsonObject o2 && o2.ContainsKey("profiles"))
            res = node.Deserialize<ProfileFile>(Json)?.Profiles;
        else res = new List<Profile> { node.Deserialize<Profile>(Json)! };
        if (res == null || res.Count == 0) throw new InvalidDataException("No profile found in file.");
        foreach (var p in res)
        {
            p.Id = Guid.NewGuid();
            p.PresetKey = null;
            var errs = ProfileValidator.Validate(p, registry);
            if (errs.Any(e => e.IsFatal)) throw new InvalidDataException(string.Join("; ", errs.Where(e => e.IsFatal).Select(e => e.Message)));
        }
        return res;
    }
}
