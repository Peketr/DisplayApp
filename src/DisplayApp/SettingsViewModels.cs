using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DisplayApp.Core;

namespace DisplayApp;

public abstract class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Editable copy of a <see cref="Preset"/>.</summary>
public sealed class PresetViewModel : ViewModel
{
    private string _name;
    private string? _hotkey;

    public PresetViewModel(Preset preset, ISet<string> connectedKeys)
    {
        _name = preset.Name;
        _hotkey = preset.Hotkey;
        SetDisplays(preset.Displays, connectedKeys);
    }

    public string Name { get => _name; set => Set(ref _name, value); }
    public string? Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public ObservableCollection<PresetDisplayViewModel> Displays { get; } = [];

    /// <summary>Raised when anything affecting the layout preview changes.</summary>
    public event Action? LayoutChanged;

    public void SetDisplays(IEnumerable<PresetDisplay> displays, ISet<string> connectedKeys)
    {
        Displays.Clear();
        foreach (var d in displays)
        {
            var vm = new PresetDisplayViewModel(d, connectedKeys.Contains(d.Key));
            vm.PropertyChanged += OnDisplayChanged;
            Displays.Add(vm);
        }
        LayoutChanged?.Invoke();
    }

    private void OnDisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        var d = (PresetDisplayViewModel)sender!;
        // Exactly one primary among enabled displays.
        if (e.PropertyName == nameof(PresetDisplayViewModel.IsPrimary) && d.IsPrimary)
        {
            foreach (var other in Displays.Where(o => o != d)) other.IsPrimary = false;
            d.Enabled = true;
        }
        if (e.PropertyName == nameof(PresetDisplayViewModel.Enabled) && !d.Enabled && d.IsPrimary)
        {
            d.IsPrimary = false;
            var next = Displays.FirstOrDefault(o => o.Enabled);
            if (next != null) next.IsPrimary = true;
        }
        LayoutChanged?.Invoke();
    }

    public Preset ToPreset() => new()
    {
        Name = Name.Trim(),
        Hotkey = string.IsNullOrEmpty(Hotkey) ? null : Hotkey,
        Displays = Displays.Select(d => d.ToModel()).ToList(),
    };
}

public sealed class PresetDisplayViewModel : ViewModel
{
    private readonly PresetDisplay _model;
    private bool _enabled;
    private bool _isPrimary;

    public PresetDisplayViewModel(PresetDisplay model, bool connected)
    {
        _model = model;
        _enabled = model.Enabled;
        _isPrimary = model.IsPrimary;
        IsConnected = connected;
    }

    public string Key => _model.Key;
    public string FriendlyName => _model.FriendlyName;
    public bool IsConnected { get; }
    public int Width => _model.Width;
    public int Height => _model.Height;
    public int PositionX => _model.PositionX;
    public int PositionY => _model.PositionY;

    /// <summary>False for displays captured while off: there is no stored layout to restore.</summary>
    public bool HasLayout => _model.Width > 0 && _model.Height > 0;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool IsPrimary { get => _isPrimary; set => Set(ref _isPrimary, value); }

    public string Mode
    {
        get
        {
            if (!HasLayout) return "no saved layout (Windows picks)";
            var hz = _model.RefreshDenominator == 0 ? 0 : Math.Round((double)_model.RefreshNumerator / _model.RefreshDenominator, 2);
            return $"{_model.Width}×{_model.Height} @ {hz} Hz  at ({_model.PositionX}, {_model.PositionY})";
        }
    }

    public string Status => IsConnected ? "" : "not connected";

    public PresetDisplay ToModel() => new()
    {
        Key = _model.Key,
        FriendlyName = _model.FriendlyName,
        Enabled = Enabled,
        IsPrimary = IsPrimary,
        Width = _model.Width,
        Height = _model.Height,
        PositionX = _model.PositionX,
        PositionY = _model.PositionY,
        RefreshNumerator = _model.RefreshNumerator,
        RefreshDenominator = _model.RefreshDenominator,
        Rotation = _model.Rotation,
    };
}

/// <summary>A per-display toggle hotkey row.</summary>
public sealed class ToggleHotkeyViewModel(string key, string friendlyName, string? hotkey, bool connected) : ViewModel
{
    private string? _hotkey = hotkey;
    public string Key { get; } = key;
    public string FriendlyName { get; } = friendlyName;
    public string Status { get; } = connected ? "" : "not connected";
    public string? Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
}
