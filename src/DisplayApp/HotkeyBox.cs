using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DisplayApp.Core;

namespace DisplayApp;

/// <summary>
/// A text box that records a key combination when focused. Press the combination to set it,
/// Backspace/Delete to clear it, Esc to leave it unchanged.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).ShowValue()));

    public string? Hotkey
    {
        get => (string?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
        Cursor = Cursors.Hand;
        ToolTip = "Click, then press a key combination. Backspace clears it.";
        ShowValue();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = "Press keys…";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowValue();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            Text = Describe(mods) + "…";
            return;
        }

        if (mods == ModifierKeys.None)
        {
            if (key is Key.Back or Key.Delete) { Hotkey = null; MoveFocusAway(); return; }
            if (key == Key.Escape) { MoveFocusAway(); return; }
            if (key == Key.Tab) { e.Handled = false; return; }
            Text = "Needs Ctrl, Alt, Shift or Win";
            return;
        }

        // AltGr arrives as Ctrl+Alt with the right Alt key; it's for typing characters, not hotkeys.
        if (mods.HasFlag(ModifierKeys.Control) && mods.HasFlag(ModifierKeys.Alt) &&
            Keyboard.IsKeyDown(Key.RightAlt) && !Keyboard.IsKeyDown(Key.LeftAlt))
        {
            Text = "AltGr isn't allowed; use left Alt";
            return;
        }

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var hotkey = new Core.Hotkey(ToHotkeyModifiers(mods), vk);
        var text = hotkey.ToString();
        if (!Core.Hotkey.TryParse(text, out _, out _))
        {
            Text = $"Key '{key}' isn't supported";
            return;
        }

        Hotkey = text;
        MoveFocusAway();
    }

    private void MoveFocusAway()
    {
        // Return focus to the window so the next key press isn't recorded again.
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
        ShowValue();
    }

    private void ShowValue() => Text = string.IsNullOrEmpty(Hotkey) ? "(none)" : Hotkey;

    private static HotkeyModifiers ToHotkeyModifiers(ModifierKeys m) =>
        (m.HasFlag(ModifierKeys.Control) ? HotkeyModifiers.Ctrl : 0) |
        (m.HasFlag(ModifierKeys.Alt) ? HotkeyModifiers.Alt : 0) |
        (m.HasFlag(ModifierKeys.Shift) ? HotkeyModifiers.Shift : 0) |
        (m.HasFlag(ModifierKeys.Windows) ? HotkeyModifiers.Win : 0);

    private static string Describe(ModifierKeys m) =>
        string.Join("+", new[]
        {
            m.HasFlag(ModifierKeys.Control) ? "Ctrl" : null,
            m.HasFlag(ModifierKeys.Alt) ? "Alt" : null,
            m.HasFlag(ModifierKeys.Shift) ? "Shift" : null,
            m.HasFlag(ModifierKeys.Windows) ? "Win" : null,
        }.Where(s => s != null)) + "+";
}
