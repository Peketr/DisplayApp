# DisplayApp

Windows tray app for switching displays on and off with global hotkeys.

- **Presets**: saved layouts (which displays are on, resolution, refresh rate, position, primary).
- **Hotkeys**: per preset, per display toggle, and an "all displays on" panic key (default `Ctrl+Alt+0`).
  AltGr is not treated as Ctrl+Alt, so AltGr+key still types its character.
- **Settings window**: double-click the tray icon.

## Projects

| Path | What |
|------|------|
| `src/DisplayApp.Core` | Display control via the Windows CCD API, presets, config, hotkey parsing |
| `src/DisplayApp` | WPF tray app, settings window, keyboard hook |
| `src/DisplayApp.Cli` | Console tool for testing (`list`, `on/off <n>`, `save/apply <name>`, `hotkey ...`) |

## Build & run

```powershell
dotnet build DisplayApp.slnx
.\publish.ps1            # -> publish\DisplayApp.exe (needs .NET 10 Desktop Runtime)
```

Config is stored in `%APPDATA%\DisplayApp\config.json`; warnings and errors in `displayapp.log` next to it.
