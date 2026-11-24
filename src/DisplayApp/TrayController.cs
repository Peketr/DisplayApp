using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using DisplayApp.Core;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DisplayApp;

/// <summary>Owns the tray icon, menu, hotkey registrations and config reloading.</summary>
public sealed class TrayController : IDisposable
{
    private readonly DisplayManager _displays = new();
    private readonly ConfigStore _store = new();
    private readonly HotkeyService _hotkeys = new();
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly Forms.NotifyIcon _icon;
    private readonly DispatcherTimer _reloadDebounce;
    private FileSystemWatcher? _watcher;
    private AppConfig _config = new();
    private SettingsWindow? _settings;

    public TrayController()
    {
        _icon = new Forms.NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "DisplayApp",
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenSettings(); };
        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); ReloadConfig(notify: true); };
    }

    public void Start()
    {
        _icon.Visible = true;
        ReloadConfig(notify: false);
        WatchConfig();
        WatchDisplays();
    }

    // ---------- display plug / unplug ----------

    private const int WM_DISPLAYCHANGE = 0x007E, WM_DEVICECHANGE = 0x0219;
    private System.Windows.Interop.HwndSource? _displayWatcher;
    private DispatcherTimer? _displayDebounce;
    private Dictionary<string, string> _knownDisplays = []; // key → friendly name

    private void WatchDisplays()
    {
        _knownDisplays = SafeConnectedDisplays() ?? [];
        _displayDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _displayDebounce.Tick += (_, _) => { _displayDebounce.Stop(); _ = OnDisplaysChanged(); };

        // A hidden top-level window: message-only windows don't receive these broadcasts.
        _displayWatcher = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("DisplayApp.DisplayWatcher")
        {
            Width = 0, Height = 0, WindowStyle = 0,
        });
        _displayWatcher.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg is WM_DISPLAYCHANGE or WM_DEVICECHANGE)
            {
                _displayDebounce.Stop();
                _displayDebounce.Start();
            }
            return IntPtr.Zero;
        });
    }

    private Dictionary<string, string>? SafeConnectedDisplays()
    {
        try { return _displays.GetDisplays().ToDictionary(d => d.Key, d => d.FriendlyName); }
        catch (DisplayConfigException) { return null; }
    }

    private async Task OnDisplaysChanged()
    {
        if (SafeConnectedDisplays() is not { } now) return;
        var added = now.Where(kv => !_knownDisplays.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var removed = _knownDisplays.Where(kv => !now.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        _knownDisplays = now;
        if (added.Count == 0 && removed.Count == 0) return; // our own layout changes land here too

        if (added.Count > 0 && _settings == null && _config.PresetOnDisplayConnected is { } name &&
            _config.FindPreset(name) is { } preset)
        {
            await Run($"Preset '{preset.Name}'", () => ApplyPreset(preset, confirm: false));
            return;
        }

        var parts = new List<string>();
        if (added.Count > 0) parts.Add($"Connected: {string.Join(", ", added)}");
        if (removed.Count > 0) parts.Add($"Disconnected: {string.Join(", ", removed)}");
        Notify("Displays changed", string.Join("\n", parts));
    }

    // ---------- config & hotkeys ----------

    private void ReloadConfig(bool notify)
    {
        try
        {
            _config = _store.Load();
        }
        catch (Exception ex)
        {
            Notify("Config error", $"Could not read config.json: {ex.Message}", Forms.ToolTipIcon.Error);
            return;
        }

        // While settings are open, hotkeys stay off so the recorder can capture them; they come back on close.
        if (_settings != null) return;

        var problems = RegisterHotkeys();
        if (problems.Count > 0)
            Notify("Some hotkeys were not registered", string.Join("\n", problems), Forms.ToolTipIcon.Warning);
        else if (notify)
            Notify("Config reloaded", $"{_config.Presets.Count} preset(s) loaded.");
    }

    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var problems = new List<string>();
        var seen = new Dictionary<Hotkey, string>();

        void Bind(string? text, string label, Func<Task> action)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!Hotkey.TryParse(text, out var hk, out var error)) { problems.Add($"{label}: {error}"); return; }
            if (seen.TryGetValue(hk, out var other)) { problems.Add($"{label}: {hk} is also assigned to {other}"); return; }
            seen[hk] = label;
            if (_hotkeys.Register(hk, () => _ = Run(label, action)) is { } reason)
                problems.Add($"{label}: {hk} {reason}");
        }

        Bind(_config.PanicHotkey, "All displays on", AllOn);
        foreach (var preset in _config.Presets)
            Bind(preset.Hotkey, $"Preset '{preset.Name}'", () => ApplyPreset(preset));
        foreach (var (displayKey, hotkey) in _config.ToggleHotkeys)
            Bind(hotkey, $"Toggle {DisplayName(displayKey)}", () => ToggleDisplay(displayKey));

        return problems;
    }

    private void WatchConfig()
    {
        var dir = Path.GetDirectoryName(_store.Path)!;
        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, Path.GetFileName(_store.Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        // ConfigStore writes a temp file and renames it, so watch renames too.
        FileSystemEventHandler changed = (_, _) => QueueReload();
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (_, _) => QueueReload();
    }

    private void QueueReload() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
    {
        _reloadDebounce.Stop();
        _reloadDebounce.Start();
    });

    // ---------- actions ----------

    /// <summary>Runs a display change off the UI thread, one at a time, reporting failures.</summary>
    private async Task Run(string label, Func<Task> action)
    {
        if (!await _busy.WaitAsync(0)) return; // ignore key presses while a change is in progress
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Notify($"{label} failed", ex.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            _busy.Release();
        }
    }

    private async Task ApplyPreset(Preset preset) => await ApplyPreset(preset, _config.ConfirmAfterHotkey);

    private async Task ApplyPreset(Preset preset, bool confirm)
    {
        var undo = confirm ? await Task.Run(() => _displays.CaptureCurrent("(previous)")) : null;
        var missing = await Task.Run(() => _displays.ApplyPreset(preset));
        if (undo != null && !KeepChangesDialog.Ask(null, TimeSpan.FromSeconds(15)))
        {
            await Task.Run(() => _displays.ApplyPreset(undo));
            Notify("Reverted", $"'{preset.Name}' was not kept; previous layout restored.");
            return;
        }
        var msg = missing.Count == 0
            ? $"Applied '{preset.Name}'."
            : $"Applied '{preset.Name}'. Not connected: {string.Join(", ", missing.Select(m => m.FriendlyName))}";
        Notify("Preset applied", msg);
    }

    private async Task ToggleDisplay(string displayKey)
    {
        var display = await Task.Run(() =>
        {
            var d = _displays.GetDisplays().FirstOrDefault(x => x.Key == displayKey)
                    ?? throw new InvalidOperationException($"{DisplayName(displayKey)} is not connected.");
            _displays.Toggle(d);
            return d;
        });
        Notify(display.FriendlyName, display.IsActive ? "Turned off." : "Turned on.");
    }

    private async Task AllOn()
    {
        await Task.Run(() =>
        {
            foreach (var d in _displays.GetDisplays().Where(d => !d.IsActive))
                _displays.SetEnabled(d, true);
        });
        Notify("All displays on", "Every connected display is enabled.");
    }

    private async Task SaveCurrentAsPreset()
    {
        var preset = await Task.Run(() => _displays.CaptureCurrent(NextPresetName()));
        var config = _store.Load();
        config.Presets.Add(preset);
        _store.Save(config); // the watcher reloads it
        Notify("Preset saved", $"Saved current layout as '{preset.Name}'. Rename it or assign a hotkey in Settings.");
    }

    // ---------- settings ----------

    public void OpenSettings()
    {
        if (_settings != null)
        {
            _settings.WindowState = System.Windows.WindowState.Normal;
            _settings.Activate();
            return;
        }

        _hotkeys.UnregisterAll();
        try
        {
            _settings = new SettingsWindow(_displays, _store, CheckHotkeyAvailable);
        }
        catch (Exception ex)
        {
            Notify("Settings failed to open", ex.Message, Forms.ToolTipIcon.Error);
            ReloadConfig(notify: false);
            return;
        }
        _settings.Closed += (_, _) =>
        {
            _settings = null;
            ReloadConfig(notify: false);
        };
        _settings.Show();
        _settings.Activate();
    }

    /// <summary>Null if no other app has the combination; otherwise a note for the user.</summary>
    private static string? CheckHotkeyAvailable(Hotkey hotkey) =>
        HotkeyService.IsUsedByAnotherApp(hotkey)
            ? "also a shortcut in another app; DisplayApp takes priority, so that app's shortcut won't work"
            : null;

    // ---------- menu ----------

    private void BuildMenu()
    {
        var menu = _icon.ContextMenuStrip!;
        menu.Items.Clear();

        var settings = AddItem(menu, "Settings…", OpenSettings);
        settings.Font = new Drawing.Font(settings.Font, Drawing.FontStyle.Bold);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var header = new Forms.ToolStripMenuItem("Presets") { Enabled = false };
        menu.Items.Add(header);
        if (_config.Presets.Count == 0)
            menu.Items.Add(new Forms.ToolStripMenuItem("(none saved)") { Enabled = false });
        foreach (var preset in _config.Presets)
        {
            var item = new Forms.ToolStripMenuItem(preset.Name) { ShortcutKeyDisplayString = preset.Hotkey };
            item.Click += (_, _) => _ = Run($"Preset '{preset.Name}'", () => ApplyPreset(preset));
            menu.Items.Add(item);
        }
        AddItem(menu, "Save current layout as preset", () => _ = Run("Save preset", SaveCurrentAsPreset));

        menu.Items.Add(new Forms.ToolStripSeparator());

        var displaysMenu = new Forms.ToolStripMenuItem("Displays");
        try
        {
            foreach (var d in _displays.GetDisplays())
            {
                var item = new Forms.ToolStripMenuItem(d.FriendlyName)
                {
                    Checked = d.IsActive,
                    ShortcutKeyDisplayString = _config.ToggleHotkeys.GetValueOrDefault(d.Key),
                };
                var key = d.Key;
                item.Click += (_, _) => _ = Run($"Toggle {d.FriendlyName}", () => ToggleDisplay(key));
                displaysMenu.DropDownItems.Add(item);
            }
        }
        catch (Exception ex)
        {
            displaysMenu.DropDownItems.Add(new Forms.ToolStripMenuItem($"Error: {ex.Message}") { Enabled = false });
        }
        menu.Items.Add(displaysMenu);

        var allOn = AddItem(menu, "All displays on", () => _ = Run("All displays on", AllOn));
        allOn.ShortcutKeyDisplayString = _config.PanicHotkey;

        menu.Items.Add(new Forms.ToolStripSeparator());
        AddItem(menu, "Open config file", OpenConfig);
        AddItem(menu, "Reload config", () => ReloadConfig(notify: true));
        AddItem(menu, "Exit", () => System.Windows.Application.Current.Shutdown());
    }

    private static Forms.ToolStripMenuItem AddItem(Forms.ContextMenuStrip menu, string text, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
        return item;
    }

    private void OpenConfig()
    {
        if (!File.Exists(_store.Path)) _store.Save(_config);
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_store.Path}\"") { UseShellExecute = true });
    }

    // ---------- helpers ----------

    private string NextPresetName()
    {
        for (int i = 1; ; i++)
            if (_config.FindPreset($"Preset {i}") == null) return $"Preset {i}";
    }

    private string DisplayName(string displayKey) =>
        _config.Presets.SelectMany(p => p.Displays).FirstOrDefault(d => d.Key == displayKey)?.FriendlyName
        ?? displayKey;

    private void Notify(string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        _icon.ShowBalloonTip(3000, title, text, icon);
        if (icon is Forms.ToolTipIcon.Error or Forms.ToolTipIcon.Warning)
        {
            try
            {
                var log = Path.Combine(Path.GetDirectoryName(_store.Path)!, "displayapp.log");
                File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{icon}] {title}: {text}{Environment.NewLine}");
            }
            catch (IOException) { }
        }
    }

    /// <summary>Loads app.ico at the tray's icon size (16 px at 100% scaling, larger on high-DPI).</summary>
    private static Drawing.Icon CreateIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico"));
        using var stream = info.Stream;
        return new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _displayDebounce?.Stop();
        _displayWatcher?.Dispose();
        _hotkeys.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
