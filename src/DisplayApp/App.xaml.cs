using System.Windows;

namespace DisplayApp;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\DisplayApp.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("DisplayApp is already running (see the system tray).", "DisplayApp",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _tray = new TrayController();
        _tray.Start();
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) _tray.OpenSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
