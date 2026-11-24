using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DisplayApp;

/// <summary>"Keep these display settings?" with a countdown that reverts automatically.</summary>
public sealed class KeepChangesDialog : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
    private int _remaining;

    /// <summary>Shows the dialog. Returns true only if the user clicked Keep before the timeout.</summary>
    public static bool Ask(Window? owner, TimeSpan timeout)
    {
        var dlg = new KeepChangesDialog((int)timeout.TotalSeconds);
        if (owner is { IsVisible: true }) dlg.Owner = owner;
        return dlg.ShowDialog() == true;
    }

    private KeepChangesDialog(int seconds)
    {
        _remaining = seconds;
        Title = "DisplayApp";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; // centre of the primary display, which is always on
        Topmost = true;
        ShowInTaskbar = true;

        var keep = new Button { Content = "Keep changes", IsDefault = true, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        var revert = new Button { Content = "Revert", IsCancel = true, Padding = new Thickness(12, 4, 12, 4) };
        keep.Click += (_, _) => { DialogResult = true; };
        revert.Click += (_, _) => { DialogResult = false; };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(keep);
        buttons.Children.Add(revert);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_message);
        root.Children.Add(buttons);
        Content = root;

        UpdateText();
        _timer.Tick += (_, _) =>
        {
            if (--_remaining <= 0) { _timer.Stop(); DialogResult = false; return; }
            UpdateText();
        };
        Loaded += (_, _) => { _timer.Start(); Activate(); keep.Focus(); };
        Closed += (_, _) => _timer.Stop();
    }

    private void UpdateText() =>
        _message.Text = $"Keep these display settings?\nReverting in {_remaining} second{(_remaining == 1 ? "" : "s")}.";
}
