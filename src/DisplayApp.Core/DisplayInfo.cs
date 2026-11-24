namespace DisplayApp.Core;

/// <summary>A physical display (monitor) known to Windows, active or not.</summary>
public sealed record DisplayInfo
{
    /// <summary>Stable identity across reboots: the monitor device path (falls back to adapter/target ids).</summary>
    public required string Key { get; init; }
    public required string FriendlyName { get; init; }
    public required long AdapterId { get; init; }
    public required uint TargetId { get; init; }
    public required bool IsActive { get; init; }

    // Only meaningful when IsActive.
    public string? GdiDeviceName { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int PositionX { get; init; }
    public int PositionY { get; init; }
    public uint RefreshNumerator { get; init; }
    public uint RefreshDenominator { get; init; }
    public uint Rotation { get; init; } = 1;
    public double RefreshRate => RefreshDenominator == 0 ? 0 : Math.Round((double)RefreshNumerator / RefreshDenominator, 2);
    public bool IsPrimary => IsActive && PositionX == 0 && PositionY == 0;
}
