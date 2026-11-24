using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DisplayApp.Core;

namespace DisplayApp;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private readonly DisplayManager _displays;
    private readonly ConfigStore _store;
    private readonly Func<Hotkey, string?> _checkAvailable;
    private AppConfig _loaded = new();
    private string _savedSnapshot = "";
    private HashSet<string> _connectedKeys = [];
    private List<DisplayInfo> _connected = [];
    private string? _panicHotkey;
    private PresetViewModel? _previewed;

    public ObservableCollection<PresetViewModel> Presets { get; } = [];
    public ObservableCollection<ToggleHotkeyViewModel> Toggles { get; } = [];

    public string? PanicHotkey
    {
        get => _panicHotkey;
        set { _panicHotkey = value; PropertyChanged?.Invoke(this, new(nameof(PanicHotkey))); }
    }

    private bool _startWithWindows, _savedStartWithWindows, _confirmAfterHotkey, _onConnectEnabled;
    private PresetViewModel? _onConnectPreset;

    public bool StartWithWindows { get => _startWithWindows; set => SetField(ref _startWithWindows, value); }
    public bool ConfirmAfterHotkey { get => _confirmAfterHotkey; set => SetField(ref _confirmAfterHotkey, value); }
    public bool OnConnectEnabled { get => _onConnectEnabled; set => SetField(ref _onConnectEnabled, value); }
    public PresetViewModel? OnConnectPreset { get => _onConnectPreset; set => SetField(ref _onConnectPreset, value); }

    public string AutostartTip => Autostart.RegisteredCommand is { } cmd && !Autostart.IsEnabled
        ? $"Currently starts a different copy: {cmd}"
        : $"Starts {Autostart.ExePath} when you sign in";

    private void SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        field = value;
        PropertyChanged?.Invoke(this, new(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <param name="checkAvailable">Returns null if a hotkey can be registered, otherwise the reason it can't.</param>
    public SettingsWindow(DisplayManager displays, ConfigStore store, Func<Hotkey, string?> checkAvailable)
    {
        _displays = displays;
        _store = store;
        _checkAvailable = checkAvailable;
        InitializeComponent();
        DataContext = this;
        Load();
    }

    // ---------- load / save ----------

    private void Load()
    {
        _connected = _displays.GetDisplays().ToList();
        _connectedKeys = _connected.Select(d => d.Key).ToHashSet();
        _loaded = _store.Load();

        Presets.Clear();
        foreach (var p in _loaded.Presets) Presets.Add(CreateVm(p));

        Toggles.Clear();
        foreach (var d in _connected)
            Toggles.Add(new ToggleHotkeyViewModel(d.Key, d.FriendlyName, _loaded.ToggleHotkeys.GetValueOrDefault(d.Key), true));
        // Keep toggles for displays that are unplugged right now.
        foreach (var (key, hk) in _loaded.ToggleHotkeys.Where(t => !_connectedKeys.Contains(t.Key)))
            Toggles.Add(new ToggleHotkeyViewModel(key, NameForKey(key), hk, false));

        PanicHotkey = _loaded.PanicHotkey;
        ConfirmAfterHotkey = _loaded.ConfirmAfterHotkey;
        OnConnectPreset = _loaded.PresetOnDisplayConnected is { } n
            ? Presets.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
            : null;
        OnConnectEnabled = OnConnectPreset != null;
        StartWithWindows = _savedStartWithWindows = Autostart.IsEnabled;
        _savedSnapshot = Snapshot(BuildConfig());
        if (Presets.Count > 0) PresetList.SelectedIndex = 0;
        UpdateEditorEnabled();
    }

    private PresetViewModel CreateVm(Preset preset)
    {
        // Connected displays the preset doesn't know about yet are listed as off.
        var displays = preset.Displays.ToList();
        foreach (var d in _connected.Where(d => displays.All(x => x.Key != d.Key)))
            displays.Add(new PresetDisplay { Key = d.Key, FriendlyName = d.FriendlyName, Enabled = false });
        preset.Displays = displays;
        return new PresetViewModel(preset, _connectedKeys);
    }

    private AppConfig BuildConfig() => new()
    {
        Presets = Presets.Select(p => p.ToPreset()).ToList(),
        ToggleHotkeys = Toggles.Where(t => !string.IsNullOrEmpty(t.Hotkey)).ToDictionary(t => t.Key, t => t.Hotkey!),
        PanicHotkey = string.IsNullOrEmpty(PanicHotkey) ? null : PanicHotkey,
        ConfirmAfterHotkey = ConfirmAfterHotkey,
        PresetOnDisplayConnected = OnConnectEnabled && OnConnectPreset != null && Presets.Contains(OnConnectPreset)
            ? OnConnectPreset.Name.Trim()
            : null,
    };

    private static string Snapshot(AppConfig c) => JsonSerializer.Serialize(c);

    /// <summary>Validates and saves. Returns false if there are blocking errors.</summary>
    private bool Save()
    {
        var config = BuildConfig();
        var errors = new List<string>();

        foreach (var p in config.Presets)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("A preset has no name.");
            if (!p.Displays.Any(d => d.Enabled)) errors.Add($"Preset '{p.Name}' has no enabled display.");
        }
        foreach (var dup in config.Presets.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Two presets are named '{dup.Key}'.");

        var assignments = new List<(string Label, string Text)>();
        if (config.PanicHotkey != null) assignments.Add(("All displays on", config.PanicHotkey));
        assignments.AddRange(config.Presets.Where(p => p.Hotkey != null).Select(p => ($"preset '{p.Name}'", p.Hotkey!)));
        assignments.AddRange(Toggles.Where(t => !string.IsNullOrEmpty(t.Hotkey)).Select(t => ($"toggle {t.FriendlyName}", t.Hotkey!)));
        foreach (var dup in assignments.GroupBy(a => a.Text).Where(g => g.Count() > 1))
            errors.Add($"{dup.Key} is assigned to {string.Join(" and ", dup.Select(a => a.Label))}.");

        if (OnConnectEnabled && config.PresetOnDisplayConnected == null)
            errors.Add("Pick the preset to apply when a display is plugged in, or untick that option.");

        if (errors.Count > 0)
        {
            ShowStatus(string.Join(" ", errors), error: true);
            return false;
        }

        if (StartWithWindows != _savedStartWithWindows)
        {
            try
            {
                Autostart.Set(StartWithWindows);
                _savedStartWithWindows = StartWithWindows;
                PropertyChanged?.Invoke(this, new(nameof(AutostartTip)));
            }
            catch (Exception ex)
            {
                ShowStatus($"Could not change 'Start with Windows': {ex.Message}", error: true);
                return false;
            }
        }

        var warnings = assignments
            .Select(a => (a.Label, a.Text, Reason: Hotkey.TryParse(a.Text, out var hk, out var err) ? _checkAvailable(hk) : err))
            .Where(a => a.Reason != null)
            .Select(a => $"{a.Text} ({a.Label}) is {a.Reason}.")
            .ToList();

        _store.Save(config);
        _savedSnapshot = Snapshot(config);
        if (warnings.Count > 0)
            ShowStatus("Saved. Note: " + string.Join(" ", warnings), error: true);
        else
            ShowStatus("Saved. Hotkeys will be active when this window closes.");
        return true;
    }

    private bool IsDirty => Snapshot(BuildConfig()) != _savedSnapshot || StartWithWindows != _savedStartWithWindows;

    // ---------- preset list ----------

    private PresetViewModel? Selected => PresetList.SelectedItem as PresetViewModel;

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_previewed != null) _previewed.LayoutChanged -= DrawPreview;
        _previewed = Selected;
        if (_previewed != null) _previewed.LayoutChanged += DrawPreview;
        UpdateEditorEnabled();
        DrawPreview();
    }

    private void UpdateEditorEnabled() => Editor.IsEnabled = Selected != null;

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var preset = _displays.CaptureCurrent(UniqueName("New preset"));
        AddAndSelect(CreateVm(preset));
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        var copy = s.ToPreset();
        copy.Name = UniqueName(copy.Name + " copy");
        copy.Hotkey = null;
        AddAndSelect(CreateVm(copy));
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        int i = PresetList.SelectedIndex;
        Presets.Remove(s);
        if (Presets.Count > 0) PresetList.SelectedIndex = Math.Min(i, Presets.Count - 1);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        int i = PresetList.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= Presets.Count) return;
        Presets.Move(i, j);
        PresetList.SelectedIndex = j;
    }

    private void AddAndSelect(PresetViewModel vm)
    {
        Presets.Add(vm);
        PresetList.SelectedItem = vm;
        PresetList.ScrollIntoView(vm);
    }

    private string UniqueName(string baseName)
    {
        if (Presets.All(p => !p.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase))) return baseName;
        for (int i = 2; ; i++)
        {
            var name = $"{baseName} {i}";
            if (Presets.All(p => !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
        }
    }

    // ---------- editor actions ----------

    private void ClearPresetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null) Selected.Hotkey = null;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        _connected = _displays.GetDisplays().ToList();
        _connectedKeys = _connected.Select(d => d.Key).ToHashSet();
        var current = Preset.FromDisplays(s.Name, _connected);
        // Keep displays the preset knows about that aren't plugged in right now.
        current.Displays.AddRange(s.ToPreset().Displays.Where(d => !_connectedKeys.Contains(d.Key)));
        s.SetDisplays(current.Displays, _connectedKeys);
        ShowStatus($"Captured current layout into '{s.Name}'. Save to keep it.");
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } s) return;
        var preset = s.ToPreset();
        if (!preset.Displays.Any(d => d.Enabled)) { ShowStatus("Enable at least one display.", error: true); return; }

        IsEnabled = false;
        try
        {
            var undo = _displays.CaptureCurrent("(previous)");
            var missing = await Task.Run(() => _displays.ApplyPreset(preset));
            bool keep = KeepChangesDialog.Ask(this, TimeSpan.FromSeconds(15));
            if (!keep)
            {
                await Task.Run(() => _displays.ApplyPreset(undo));
                ShowStatus("Reverted to the previous layout.");
            }
            else
            {
                ShowStatus(missing.Count == 0
                    ? $"Applied '{preset.Name}'."
                    : $"Applied '{preset.Name}'. Not connected: {string.Join(", ", missing.Select(m => m.FriendlyName))}");
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Test failed: {ex.Message}", error: true);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    // ---------- layout preview ----------

    private void Preview_SizeChanged(object sender, SizeChangedEventArgs e) => DrawPreview();

    private void DrawPreview()
    {
        Preview.Children.Clear();
        if (Selected is not { } s) return;

        var shown = s.Displays.Where(d => d.Enabled && d.HasLayout).ToList();
        var noLayout = s.Displays.Where(d => d.Enabled && !d.HasLayout).ToList();
        if (shown.Count == 0)
        {
            AddLabel("No layout to show. Turn displays on, then use 'Capture current layout'.", 10, 10);
            return;
        }

        double minX = shown.Min(d => d.PositionX), minY = shown.Min(d => d.PositionY);
        double maxX = shown.Max(d => d.PositionX + d.Width), maxY = shown.Max(d => d.PositionY + d.Height);
        const double pad = 16;
        double w = Math.Max(1, Preview.ActualWidth - 2 * pad), h = Math.Max(1, Preview.ActualHeight - 2 * pad - (noLayout.Count > 0 ? 20 : 0));
        double scale = Math.Min(w / (maxX - minX), h / (maxY - minY));
        double offX = pad + (w - (maxX - minX) * scale) / 2, offY = pad + (h - (maxY - minY) * scale) / 2;

        foreach (var d in shown)
        {
            var box = new Border
            {
                Width = Math.Max(4, d.Width * scale - 3),
                Height = Math.Max(4, d.Height * scale - 3),
                Background = d.IsPrimary ? new SolidColorBrush(Color.FromRgb(40, 120, 220)) : new SolidColorBrush(Color.FromRgb(90, 150, 230)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = new TextBlock
                {
                    Text = $"{d.FriendlyName}{(d.IsPrimary ? "  ★" : "")}\n{d.Width}×{d.Height}",
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4),
                },
                ToolTip = d.Mode,
            };
            Canvas.SetLeft(box, offX + (d.PositionX - minX) * scale);
            Canvas.SetTop(box, offY + (d.PositionY - minY) * scale);
            Preview.Children.Add(box);
        }

        if (noLayout.Count > 0)
            AddLabel($"Also on (Windows chooses layout): {string.Join(", ", noLayout.Select(d => d.FriendlyName))}",
                pad, Preview.ActualHeight - 22);
    }

    private void AddLabel(string text, double x, double y)
    {
        var tb = new TextBlock { Text = text, Foreground = Brushes.Gray };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        Preview.Children.Add(tb);
    }

    // ---------- footer ----------

    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!IsDirty) return;
        var answer = MessageBox.Show(this, "Save changes before closing?", "DisplayApp",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !Save()))
            e.Cancel = true;
    }

    private void ShowStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = error ? Brushes.Firebrick : Brushes.DimGray;
    }

    private string NameForKey(string key) =>
        _loaded.Presets.SelectMany(p => p.Displays).FirstOrDefault(d => d.Key == key)?.FriendlyName ?? key;
}
