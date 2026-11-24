namespace DisplayApp.Core;

/// <summary>A saved display layout that can be applied in one step.</summary>
public sealed class Preset
{
    public required string Name { get; set; }

    /// <summary>Hotkey such as "Ctrl+Alt+1"; assigned in the UI (milestone 3/4).</summary>
    public string? Hotkey { get; set; }

    public List<PresetDisplay> Displays { get; set; } = [];

    /// <summary>Snapshot of the current configuration. Displays that are off are stored as disabled.</summary>
    public static Preset FromDisplays(string name, IEnumerable<DisplayInfo> displays) => new()
    {
        Name = name,
        Displays = displays.Select(d => new PresetDisplay
        {
            Key = d.Key,
            FriendlyName = d.FriendlyName,
            Enabled = d.IsActive,
            IsPrimary = d.IsPrimary,
            Width = d.Width,
            Height = d.Height,
            PositionX = d.PositionX,
            PositionY = d.PositionY,
            RefreshNumerator = d.RefreshNumerator,
            RefreshDenominator = d.RefreshDenominator,
            Rotation = d.Rotation,
        }).ToList(),
    };
}

public sealed class PresetDisplay
{
    /// <summary>Monitor device path; matches <see cref="DisplayInfo.Key"/>.</summary>
    public required string Key { get; set; }
    public string FriendlyName { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsPrimary { get; set; }

    // Layout, used when Enabled. Width/Height are desktop size (as shown in Windows settings).
    public int Width { get; set; }
    public int Height { get; set; }
    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public uint RefreshNumerator { get; set; }
    public uint RefreshDenominator { get; set; }
    /// <summary>1 = landscape, 2 = 90°, 3 = 180°, 4 = 270° (DISPLAYCONFIG_ROTATION).</summary>
    public uint Rotation { get; set; } = 1;
}
