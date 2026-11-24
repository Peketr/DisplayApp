using System.Runtime.InteropServices;

namespace DisplayApp.Core.Native;

// P/Invoke definitions for the Windows Connecting and Configuring Displays (CCD) API.
// https://learn.microsoft.com/windows-hardware/drivers/display/ccd-apis

internal static class CcdApi
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const uint PATH_MODE_IDX_INVALID = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(QueryDisplayConfigFlags flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        QueryDisplayConfigFlags flags,
        ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[]? pathArray,
        uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[]? modeInfoArray,
        SetDisplayConfigFlags flags);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);
}

[Flags]
internal enum QueryDisplayConfigFlags : uint
{
    AllPaths = 0x1,
    OnlyActivePaths = 0x2,
    DatabaseCurrent = 0x4,
}

[Flags]
internal enum SetDisplayConfigFlags : uint
{
    TopologyInternal = 0x1,
    TopologyClone = 0x2,
    TopologyExtend = 0x4,
    TopologyExternal = 0x8,
    TopologySupplied = 0x10,
    UseSuppliedDisplayConfig = 0x20,
    Validate = 0x40,
    Apply = 0x80,
    NoOptimization = 0x100,
    SaveToDatabase = 0x200,
    AllowChanges = 0x400,
    PathPersistIfRequired = 0x800,
    ForceModeEnumeration = 0x1000,
    AllowPathOrderChanges = 0x2000,
}

[Flags]
internal enum PathFlags : uint
{
    Active = 0x1,
}

internal enum ModeInfoType : uint
{
    Source = 1,
    Target = 2,
    DesktopImage = 3,
}

internal enum DeviceInfoType : uint
{
    GetSourceName = 1,
    GetTargetName = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;

    public readonly long Value => ((long)HighPart << 32) | LowPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_SOURCE_INFO
{
    public LUID AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_TARGET_INFO
{
    public LUID AdapterId;
    public uint Id;
    public uint ModeInfoIdx;
    public uint OutputTechnology;
    public uint Rotation;
    public uint Scaling;
    public DISPLAYCONFIG_RATIONAL RefreshRate;
    public uint ScanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
    public uint StatusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_PATH_INFO
{
    public DISPLAYCONFIG_PATH_SOURCE_INFO SourceInfo;
    public DISPLAYCONFIG_PATH_TARGET_INFO TargetInfo;
    public PathFlags Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_2DREGION
{
    public uint Cx;
    public uint Cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_SOURCE_MODE
{
    public uint Width;
    public uint Height;
    public uint PixelFormat;
    public POINTL Position;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
{
    public ulong PixelRate;
    public DISPLAYCONFIG_RATIONAL HSyncFreq;
    public DISPLAYCONFIG_RATIONAL VSyncFreq;
    public DISPLAYCONFIG_2DREGION ActiveSize;
    public DISPLAYCONFIG_2DREGION TotalSize;
    public uint VideoStandard;
    public uint ScanLineOrdering;
}

[StructLayout(LayoutKind.Explicit)]
internal struct DISPLAYCONFIG_MODE_INFO
{
    [FieldOffset(0)] public ModeInfoType InfoType;
    [FieldOffset(4)] public uint Id;
    [FieldOffset(8)] public LUID AdapterId;
    [FieldOffset(16)] public DISPLAYCONFIG_VIDEO_SIGNAL_INFO TargetMode;
    [FieldOffset(16)] public DISPLAYCONFIG_SOURCE_MODE SourceMode;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPLAYCONFIG_DEVICE_INFO_HEADER
{
    public DeviceInfoType Type;
    public uint Size;
    public LUID AdapterId;
    public uint Id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_TARGET_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
    public uint Flags;
    public uint OutputTechnology;
    public ushort EdidManufactureId;
    public ushort EdidProductCodeId;
    public uint ConnectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
{
    public DISPLAYCONFIG_DEVICE_INFO_HEADER Header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
}
