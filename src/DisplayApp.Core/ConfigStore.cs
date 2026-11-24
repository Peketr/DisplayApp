using System.Text.Json;
using System.Text.Json.Serialization;

namespace DisplayApp.Core;

public sealed class AppConfig
{
    public List<Preset> Presets { get; set; } = [];

    /// <summary>Per-display toggle hotkeys: display key → hotkey string (milestone 3/4).</summary>
    public Dictionary<string, string> ToggleHotkeys { get; set; } = [];

    public string? PanicHotkey { get; set; } = "Ctrl+Alt+0";

    /// <summary>Show the 15-second "keep these settings?" prompt after a preset is applied from a hotkey or the tray.</summary>
    public bool ConfirmAfterHotkey { get; set; }

    /// <summary>Preset applied automatically when a new display is plugged in (null = do nothing).</summary>
    public string? PresetOnDisplayConnected { get; set; }

    public Preset? FindPreset(string name) =>
        Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Loads and saves <see cref="AppConfig"/> as JSON under %APPDATA%\DisplayApp.</summary>
public sealed class ConfigStore(string? path = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep "Ctrl+Alt+1" readable instead of "Ctrl+Alt+1" (file is local, never served as HTML).
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Path { get; } = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayApp", "config.json");

    public AppConfig Load()
    {
        if (!File.Exists(Path)) return new AppConfig();
        using var stream = File.OpenRead(Path);
        return JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        // Write to a temp file first so a crash never leaves a half-written config.
        var tmp = Path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tmp, Path, overwrite: true);
    }
}
