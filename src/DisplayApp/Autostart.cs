using Microsoft.Win32;

namespace DisplayApp;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayApp";

    public static string ExePath => Environment.ProcessPath!;

    /// <summary>The command currently registered, or null.</summary>
    public static string? RegisteredCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) as string;
        }
    }

    /// <summary>True only if autostart points at this exe.</summary>
    public static bool IsEnabled =>
        RegisteredCommand?.Contains(ExePath, StringComparison.OrdinalIgnoreCase) == true;

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{ExePath}\"");
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
