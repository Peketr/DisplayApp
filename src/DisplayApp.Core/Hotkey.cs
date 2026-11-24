namespace DisplayApp.Core;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A key combination such as "Ctrl+Alt+1", parsed into Win32 modifier flags and a virtual-key code.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly Dictionary<string, uint> NamedKeys = BuildKeyTable();

    public static Hotkey Parse(string text) =>
        TryParse(text, out var hk, out var error) ? hk : throw new FormatException(error);

    public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
    {
        hotkey = default;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Hotkey is empty."; return false; }

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var mods = HotkeyModifiers.None;
        uint? vk = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows" or "meta": mods |= HotkeyModifiers.Win; continue;
            }
            if (vk != null) { error = $"'{text}' has more than one non-modifier key."; return false; }
            if (!NamedKeys.TryGetValue(part.ToUpperInvariant(), out var code))
            {
                error = $"Unknown key '{part}' in '{text}'.";
                return false;
            }
            vk = code;
        }

        if (vk == null) { error = $"'{text}' has no key, only modifiers."; return false; }
        if (mods == HotkeyModifiers.None) { error = $"'{text}' needs at least one modifier (Ctrl, Alt, Shift, Win)."; return false; }

        hotkey = new Hotkey(mods, vk.Value);
        return true;
    }

    public static string KeyName(uint vk) =>
        NamedKeys.FirstOrDefault(kv => kv.Value == vk).Key is { } name ? name : $"0x{vk:X2}";

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    private static Dictionary<string, uint> BuildKeyTable()
    {
        var t = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (char c = 'A'; c <= 'Z'; c++) t[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) t[c.ToString()] = c;
        for (uint i = 1; i <= 24; i++) t[$"F{i}"] = 0x6F + i;          // VK_F1 = 0x70
        for (uint i = 0; i <= 9; i++) t[$"NUM{i}"] = 0x60 + i;         // VK_NUMPAD0
        t["SPACE"] = 0x20; t["PAGEUP"] = 0x21; t["PAGEDOWN"] = 0x22; t["END"] = 0x23; t["HOME"] = 0x24;
        t["LEFT"] = 0x25; t["UP"] = 0x26; t["RIGHT"] = 0x27; t["DOWN"] = 0x28;
        t["INSERT"] = 0x2D; t["DELETE"] = 0x2E; t["PAUSE"] = 0x13; t["ESC"] = 0x1B; t["TAB"] = 0x09;
        t["ENTER"] = 0x0D; t["BACKSPACE"] = 0x08; t["PRINTSCREEN"] = 0x2C; t["SCROLLLOCK"] = 0x91;
        t["NUM*"] = 0x6A; t["NUM+"] = 0x6B; t["NUM-"] = 0x6D; t["NUM."] = 0x6E; t["NUM/"] = 0x6F;
        t[";"] = 0xBA; t["="] = 0xBB; t[","] = 0xBC; t["-"] = 0xBD; t["."] = 0xBE; t["/"] = 0xBF; t["`"] = 0xC0;
        t["["] = 0xDB; t["\\"] = 0xDC; t["]"] = 0xDD; t["'"] = 0xDE;
        return t;
    }
}
