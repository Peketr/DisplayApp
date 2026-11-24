using System.ComponentModel;
using System.Runtime.InteropServices;
using DisplayApp.Core.Native;

namespace DisplayApp.Core;

public sealed class DisplayConfigException(string operation, int error)
    : Exception($"{operation} failed: {new Win32Exception(error).Message} (error {error})")
{
    public int ErrorCode { get; } = error;
}

/// <summary>Enumerates displays and turns them on/off using the CCD API.</summary>
public sealed class DisplayManager
{
    private const SetDisplayConfigFlags ApplyFlags =
        SetDisplayConfigFlags.Apply |
        SetDisplayConfigFlags.UseSuppliedDisplayConfig |
        SetDisplayConfigFlags.AllowChanges |
        SetDisplayConfigFlags.SaveToDatabase;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var (paths, modes) = Query(QueryDisplayConfigFlags.AllPaths);
        var result = new Dictionary<(long, uint), DisplayInfo>();

        foreach (var path in paths)
        {
            var t = path.TargetInfo;
            if (!t.TargetAvailable) continue;

            var id = (t.AdapterId.Value, t.Id);
            bool active = path.Flags.HasFlag(PathFlags.Active);

            // ALL_PATHS lists one path per (source, target) combination; prefer the active one.
            if (result.TryGetValue(id, out var existing) && (existing.IsActive || !active)) continue;

            var name = GetTargetName(t.AdapterId, t.Id);
            var info = new DisplayInfo
            {
                Key = string.IsNullOrEmpty(name.MonitorDevicePath) ? $"{t.AdapterId.Value:X}:{t.Id}" : name.MonitorDevicePath,
                FriendlyName = string.IsNullOrEmpty(name.MonitorFriendlyDeviceName) ? $"Display {t.Id}" : name.MonitorFriendlyDeviceName,
                AdapterId = t.AdapterId.Value,
                TargetId = t.Id,
                IsActive = active,
            };

            if (active)
            {
                info = info with
                {
                    GdiDeviceName = GetSourceName(path.SourceInfo.AdapterId, path.SourceInfo.Id),
                    RefreshNumerator = t.RefreshRate.Numerator,
                    RefreshDenominator = t.RefreshRate.Denominator,
                    Rotation = t.Rotation,
                };
                if (path.SourceInfo.ModeInfoIdx != CcdApi.PATH_MODE_IDX_INVALID &&
                    path.SourceInfo.ModeInfoIdx < modes.Length)
                {
                    var sm = modes[path.SourceInfo.ModeInfoIdx].SourceMode;
                    info = info with
                    {
                        Width = (int)sm.Width,
                        Height = (int)sm.Height,
                        PositionX = sm.Position.X,
                        PositionY = sm.Position.Y,
                    };
                }
            }

            result[id] = info;
        }

        return result.Values
            .OrderByDescending(d => d.IsActive)
            .ThenBy(d => d.PositionX)
            .ThenBy(d => d.FriendlyName)
            .ToList();
    }

    public Preset CaptureCurrent(string name) => Preset.FromDisplays(name, GetDisplays());

    /// <summary>
    /// Applies a preset exactly: enabled displays get the stored resolution, refresh rate, rotation and
    /// position; every other connected display is turned off. Displays in the preset that aren't
    /// connected are skipped and returned.
    /// </summary>
    public IReadOnlyList<PresetDisplay> ApplyPreset(Preset preset)
    {
        var connected = GetDisplays().ToDictionary(d => d.Key);
        var wanted = preset.Displays.Where(p => p.Enabled && connected.ContainsKey(p.Key)).ToList();
        var missing = preset.Displays.Where(p => p.Enabled && !connected.ContainsKey(p.Key)).ToList();
        if (wanted.Count == 0)
            throw new InvalidOperationException($"None of the displays in preset '{preset.Name}' are connected.");

        // Windows rejects activating a display and supplying its exact mode in the same call.
        // Step 1: fix the topology (which displays are on), letting Windows pick modes for new ones.
        // Step 2 (below): set exact resolution/refresh/position on the now-active displays.
        if (wanted.Any(w => !connected[w.Key].IsActive))
        {
            SetTopology(wanted.Select(w => connected[w.Key]).ToList());
            connected = GetDisplays().ToDictionary(d => d.Key);
        }

        // Windows requires the primary display at (0,0); shift everything relative to it.
        var primary = wanted.FirstOrDefault(w => w.IsPrimary) ?? wanted[0];
        int dx = primary.PositionX, dy = primary.PositionY;

        var (paths, modes) = Query(QueryDisplayConfigFlags.AllPaths);
        var usedSources = new HashSet<(long, uint)>();
        var newPaths = new List<DISPLAYCONFIG_PATH_INFO>();
        var newModes = new List<DISPLAYCONFIG_MODE_INFO>();

        // Displays that are already on go first so they keep their current source (less flicker).
        foreach (var stored in wanted.OrderByDescending(w => connected[w.Key].IsActive))
        {
            var d = connected[stored.Key];
            // A display captured while off has no stored layout: keep whatever Windows gave it in step 1.
            var w = stored.Width > 0 && stored.Height > 0 ? stored : new PresetDisplay
            {
                Key = d.Key, Width = d.Width, Height = d.Height,
                PositionX = d.PositionX + dx, PositionY = d.PositionY + dy,
                RefreshNumerator = d.RefreshNumerator, RefreshDenominator = d.RefreshDenominator, Rotation = d.Rotation,
            };
            var candidates = paths.Where(p => IsTarget(p, d) && p.TargetInfo.TargetAvailable &&
                                              !usedSources.Contains((p.SourceInfo.AdapterId.Value, p.SourceInfo.Id)))
                                  .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException($"No free display source available for '{d.FriendlyName}'.");

            var original = candidates.FirstOrDefault(p => p.Flags.HasFlag(PathFlags.Active), candidates[0]);
            bool wasActive = original.Flags.HasFlag(PathFlags.Active);
            usedSources.Add((original.SourceInfo.AdapterId.Value, original.SourceInfo.Id));

            var path = original;
            path.Flags = PathFlags.Active;

            // Source mode: desktop size and position.
            var srcMode = new DISPLAYCONFIG_MODE_INFO
            {
                InfoType = ModeInfoType.Source,
                Id = path.SourceInfo.Id,
                AdapterId = path.SourceInfo.AdapterId,
                SourceMode = new DISPLAYCONFIG_SOURCE_MODE
                {
                    Width = (uint)w.Width,
                    Height = (uint)w.Height,
                    PixelFormat = 4, // DISPLAYCONFIG_PIXELFORMAT_32BPP
                    Position = new POINTL { X = w.PositionX - dx, Y = w.PositionY - dy },
                },
            };
            path.SourceInfo.ModeInfoIdx = (uint)newModes.Count;
            newModes.Add(srcMode);

            // Target mode: reuse the current signal if nothing about it changes, otherwise let
            // Windows pick one matching the source size and the refresh rate set on the path.
            bool sameSignal = wasActive && d.Width == w.Width && d.Height == w.Height &&
                              d.RefreshNumerator == w.RefreshNumerator &&
                              d.RefreshDenominator == w.RefreshDenominator && d.Rotation == w.Rotation &&
                              original.TargetInfo.ModeInfoIdx < modes.Length;
            if (sameSignal)
            {
                path.TargetInfo.ModeInfoIdx = (uint)newModes.Count;
                newModes.Add(modes[original.TargetInfo.ModeInfoIdx]);
            }
            else
            {
                path.TargetInfo.ModeInfoIdx = CcdApi.PATH_MODE_IDX_INVALID;
                if (w.RefreshDenominator != 0)
                    path.TargetInfo.RefreshRate = new DISPLAYCONFIG_RATIONAL
                        { Numerator = w.RefreshNumerator, Denominator = w.RefreshDenominator };
                path.TargetInfo.Rotation = w.Rotation == 0 ? 1 : w.Rotation;
            }

            newPaths.Add(path);
        }

        Apply(newPaths.ToArray(), newModes.ToArray());
        return missing;
    }

    /// <summary>Makes exactly <paramref name="displays"/> active; new ones get modes chosen by Windows.</summary>
    private static void SetTopology(IReadOnlyList<DisplayInfo> displays)
    {
        var (paths, modes) = Query(QueryDisplayConfigFlags.AllPaths);
        var result = new List<DISPLAYCONFIG_PATH_INFO>();
        var usedSources = new HashSet<(long, uint)>();

        // Keep active paths of displays that stay on, with their current modes.
        foreach (var p in paths.Where(p => p.Flags.HasFlag(PathFlags.Active) && displays.Any(d => IsTarget(p, d))))
        {
            result.Add(p);
            usedSources.Add((p.SourceInfo.AdapterId.Value, p.SourceInfo.Id));
        }

        foreach (var d in displays.Where(d => !result.Any(p => IsTarget(p, d))))
        {
            var candidate = paths
                .Where(p => IsTarget(p, d) && p.TargetInfo.TargetAvailable)
                .Cast<DISPLAYCONFIG_PATH_INFO?>()
                .FirstOrDefault(p => !usedSources.Contains((p!.Value.SourceInfo.AdapterId.Value, p.Value.SourceInfo.Id)))
                ?? throw new InvalidOperationException($"No free display source available for '{d.FriendlyName}'.");

            var path = candidate;
            path.Flags |= PathFlags.Active;
            path.SourceInfo.ModeInfoIdx = CcdApi.PATH_MODE_IDX_INVALID;
            path.TargetInfo.ModeInfoIdx = CcdApi.PATH_MODE_IDX_INVALID;
            usedSources.Add((path.SourceInfo.AdapterId.Value, path.SourceInfo.Id));
            result.Add(path);
        }

        Apply(result.ToArray(), modes);
    }

    public void SetEnabled(DisplayInfo display, bool enabled)
    {
        if (enabled) Enable(display); else Disable(display);
    }

    public void Toggle(DisplayInfo display) => SetEnabled(display, !display.IsActive);

    private static void Disable(DisplayInfo display)
    {
        var (paths, modes) = Query(QueryDisplayConfigFlags.OnlyActivePaths);
        var remaining = paths.Where(p => !IsTarget(p, display)).ToArray();

        if (remaining.Length == paths.Length) return; // already off
        if (remaining.Length == 0)
            throw new InvalidOperationException("Refusing to disable the last active display.");

        Apply(remaining, modes);
    }

    private static void Enable(DisplayInfo display)
    {
        var (paths, modes) = Query(QueryDisplayConfigFlags.AllPaths);
        var active = paths.Where(p => p.Flags.HasFlag(PathFlags.Active)).ToList();

        if (active.Any(p => IsTarget(p, display))) return; // already on

        // Pick a path to this target whose source isn't already driving another display.
        var usedSources = active.Select(p => (p.SourceInfo.AdapterId.Value, p.SourceInfo.Id)).ToHashSet();
        var candidate = paths
            .Where(p => IsTarget(p, display) && p.TargetInfo.TargetAvailable)
            .Cast<DISPLAYCONFIG_PATH_INFO?>()
            .FirstOrDefault(p => !usedSources.Contains((p!.Value.SourceInfo.AdapterId.Value, p.Value.SourceInfo.Id)))
            ?? throw new InvalidOperationException($"No free display source available for '{display.FriendlyName}'.");

        var path = candidate;
        path.Flags |= PathFlags.Active;
        // Let Windows choose resolution/position (restored from its database when possible).
        path.SourceInfo.ModeInfoIdx = CcdApi.PATH_MODE_IDX_INVALID;
        path.TargetInfo.ModeInfoIdx = CcdApi.PATH_MODE_IDX_INVALID;
        active.Add(path);

        Apply(active.ToArray(), modes);
    }

    private static bool IsTarget(DISPLAYCONFIG_PATH_INFO p, DisplayInfo d) =>
        p.TargetInfo.AdapterId.Value == d.AdapterId && p.TargetInfo.Id == d.TargetId;

    private static void Apply(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes)
    {
        int err = CcdApi.SetDisplayConfig((uint)paths.Length, paths, (uint)modes.Length, modes, ApplyFlags);
        if (err != CcdApi.ERROR_SUCCESS) throw new DisplayConfigException("SetDisplayConfig", err);
    }

    private static (DISPLAYCONFIG_PATH_INFO[] Paths, DISPLAYCONFIG_MODE_INFO[] Modes) Query(QueryDisplayConfigFlags flags)
    {
        while (true)
        {
            int err = CcdApi.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (err != CcdApi.ERROR_SUCCESS) throw new DisplayConfigException("GetDisplayConfigBufferSizes", err);

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            err = CcdApi.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (err == CcdApi.ERROR_INSUFFICIENT_BUFFER) continue; // config changed between calls
            if (err != CcdApi.ERROR_SUCCESS) throw new DisplayConfigException("QueryDisplayConfig", err);

            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    private static DISPLAYCONFIG_TARGET_DEVICE_NAME GetTargetName(LUID adapter, uint targetId)
    {
        var req = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            Header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                Type = DeviceInfoType.GetTargetName,
                Size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                AdapterId = adapter,
                Id = targetId,
            },
        };
        CcdApi.DisplayConfigGetDeviceInfo(ref req); // failure leaves strings empty; caller falls back
        return req;
    }

    private static string? GetSourceName(LUID adapter, uint sourceId)
    {
        var req = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            Header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                Type = DeviceInfoType.GetSourceName,
                Size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                AdapterId = adapter,
                Id = sourceId,
            },
        };
        return CcdApi.DisplayConfigGetDeviceInfo(ref req) == CcdApi.ERROR_SUCCESS ? req.ViewGdiDeviceName : null;
    }
}
