using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DisplayApp.Core;

namespace DisplayApp;

/// <summary>
/// System-wide hotkeys via a low-level keyboard hook (WH_KEYBOARD_LL) instead of RegisterHotKey.
/// The hook sees left/right modifiers separately, so AltGr (which Windows reports as Ctrl+Alt)
/// is not treated as Ctrl+Alt: Ctrl+Alt+1 fires the hotkey, AltGr+1 still types its character.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3,
                       VK_LMENU = 0xA4, VK_RMENU = 0xA5, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const uint VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const uint AltGrFakeCtrlScanCode = 0x21D; // the synthetic LCtrl Windows sends before AltGr
    private const ushort VK_MASK_KEY = 0xE8;          // unassigned VK, used to stop Alt/Win opening menus

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly LowLevelKeyboardProc _proc; // kept alive so the GC doesn't collect the callback
    private readonly IntPtr _hook;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<Hotkey, Action> _handlers = [];
    private readonly HashSet<uint> _down = [];        // keys currently held (real, as seen by the hook)
    private readonly HashSet<uint> _swallowed = [];   // keys whose down we ate; eat their up too
    private bool _altGrDown;

    public HotkeyService()
    {
        _proc = HookProc;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Could not install keyboard hook (error {Marshal.GetLastWin32Error()}).");
    }

    /// <summary>Registers a hotkey. Returns null on success, or a reason it failed.</summary>
    public string? Register(Hotkey hotkey, Action handler)
    {
        if (!_handlers.TryAdd(hotkey, handler)) return "already assigned in DisplayApp";
        return null;
    }

    public void UnregisterAll() => _handlers.Clear();

    /// <summary>
    /// Whether another app has claimed this combination with RegisterHotKey. DisplayApp's hook runs
    /// first, so it still works, but the other app's shortcut will stop working while DisplayApp runs.
    /// </summary>
    public static bool IsUsedByAnotherApp(Hotkey hotkey)
    {
        const int probeId = 0xBEEF;
        if (!RegisterHotKey(IntPtr.Zero, probeId, (uint)hotkey.Modifiers | 0x4000, hotkey.VirtualKey)) return true;
        UnregisterHotKey(IntPtr.Zero, probeId);
        return false;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int msg = wParam.ToInt32();
        bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
        uint vk = k.vkCode;

        // Our own masking key press: pass through untouched.
        if (vk == VK_MASK_KEY && (k.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        // AltGr = synthetic LCtrl (scan code 0x21D) + RAlt. Remember it so neither counts as a modifier.
        if (vk == VK_LCONTROL && k.scanCode == AltGrFakeCtrlScanCode)
        {
            if (isDown) _altGrDown = true;
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        if (vk == VK_RMENU && isUp) _altGrDown = false;

        if (IsModifier(vk))
        {
            if (isDown) _down.Add(vk); else if (isUp) _down.Remove(vk);
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (isUp)
        {
            bool eat = _swallowed.Remove(vk);
            _down.Remove(vk);
            return eat ? 1 : CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (isDown && _handlers.Count > 0 && _handlers.TryGetValue(new Hotkey(CurrentModifiers(), vk), out var handler))
        {
            bool repeat = !_down.Add(vk);
            _swallowed.Add(vk);
            if (!repeat)
            {
                // Releasing Alt/Win after a swallowed key would open the menu bar / Start menu; a dummy key prevents it.
                if (_down.Overlaps([VK_LMENU, VK_RMENU, VK_LWIN, VK_RWIN]))
                {
                    keybd_event((byte)VK_MASK_KEY, 0, 0, UIntPtr.Zero);
                    keybd_event((byte)VK_MASK_KEY, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
                }
                // Never run the action inside the hook: Windows removes hooks that take too long.
                _dispatcher.BeginInvoke(handler);
            }
            return 1;
        }

        if (isDown) _down.Add(vk);
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

    private HotkeyModifiers CurrentModifiers()
    {
        // Key-ups can be missed (lock screen, UAC prompt); drop modifiers Windows says are no longer held.
        _down.RemoveWhere(vk => IsModifier(vk) && (GetAsyncKeyState((int)vk) & 0x8000) == 0);
        if (!_down.Contains(VK_RMENU)) _altGrDown = false;

        var m = HotkeyModifiers.None;
        if (_down.Contains(VK_LCONTROL) || _down.Contains(VK_RCONTROL)) m |= HotkeyModifiers.Ctrl;
        // Right Alt counts as Alt only when it isn't acting as AltGr.
        if (_down.Contains(VK_LMENU) || (_down.Contains(VK_RMENU) && !_altGrDown)) m |= HotkeyModifiers.Alt;
        if (_down.Contains(VK_LSHIFT) || _down.Contains(VK_RSHIFT)) m |= HotkeyModifiers.Shift;
        if (_down.Contains(VK_LWIN) || _down.Contains(VK_RWIN)) m |= HotkeyModifiers.Win;
        return m;
    }

    private static bool IsModifier(uint vk) => vk is VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL
        or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN or VK_SHIFT or VK_CONTROL or VK_MENU;

    public void Dispose()
    {
        _handlers.Clear();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }
}
