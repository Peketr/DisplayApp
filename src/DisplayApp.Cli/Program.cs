using DisplayApp.Core;

// Console prototype for the core library.
//   list                     show displays
//   toggle|on|off <n>        change one display
//   all-on                   turn every display on
//   presets                  list saved presets
//   save <name>              save current layout as a preset (overwrites)
//   apply <name> [--yes]     apply a preset; reverts after 15 s unless confirmed
//   delete <name>            remove a preset

var manager = new DisplayManager();
var store = new ConfigStore();
var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
string? arg1 = args.Length > 1 ? args[1] : null;

try
{
    switch (cmd)
    {
        case "list":
            PrintDisplays(manager.GetDisplays());
            return 0;

        case "toggle" or "on" or "off":
        {
            var displays = manager.GetDisplays();
            if (!int.TryParse(arg1, out int n) || n < 1 || n > displays.Count)
            {
                Console.Error.WriteLine($"Usage: {cmd} <1..{displays.Count}>");
                PrintDisplays(displays);
                return 2;
            }
            var d = displays[n - 1];
            if (cmd == "toggle") manager.Toggle(d); else manager.SetEnabled(d, cmd == "on");
            Console.WriteLine($"Done: {d.FriendlyName}");
            PrintDisplays(manager.GetDisplays());
            return 0;
        }

        case "all-on":
            foreach (var d in manager.GetDisplays().Where(d => !d.IsActive))
                manager.SetEnabled(d, true);
            PrintDisplays(manager.GetDisplays());
            return 0;

        case "presets":
        {
            var config = store.Load();
            Console.WriteLine($"Config: {store.Path}");
            if (config.Presets.Count == 0) Console.WriteLine("  (no presets)");
            foreach (var p in config.Presets) PrintPreset(p);
            return 0;
        }

        case "save":
        {
            if (string.IsNullOrWhiteSpace(arg1)) { Console.Error.WriteLine("Usage: save <name>"); return 2; }
            var config = store.Load();
            var preset = manager.CaptureCurrent(arg1);
            var existing = config.FindPreset(arg1);
            if (existing != null)
            {
                preset.Hotkey = existing.Hotkey; // keep assigned hotkey when re-capturing
                config.Presets[config.Presets.IndexOf(existing)] = preset;
            }
            else config.Presets.Add(preset);
            store.Save(config);
            Console.WriteLine($"Saved preset '{arg1}' to {store.Path}");
            PrintPreset(preset);
            return 0;
        }

        case "delete":
        {
            var config = store.Load();
            var preset = arg1 == null ? null : config.FindPreset(arg1);
            if (preset == null) { Console.Error.WriteLine($"No preset named '{arg1}'."); return 1; }
            config.Presets.Remove(preset);
            store.Save(config);
            Console.WriteLine($"Deleted preset '{preset.Name}'.");
            return 0;
        }

        case "apply":
        {
            var preset = arg1 == null ? null : store.Load().FindPreset(arg1);
            if (preset == null) { Console.Error.WriteLine($"No preset named '{arg1}'. Run 'presets' to list."); return 1; }

            var undo = manager.CaptureCurrent("(previous)");
            var missing = manager.ApplyPreset(preset);
            foreach (var m in missing) Console.WriteLine($"  Skipped (not connected): {m.FriendlyName}");
            PrintDisplays(manager.GetDisplays());

            bool confirm = !args.Contains("--yes") && !Console.IsInputRedirected;
            if (confirm && !WaitForConfirmation(TimeSpan.FromSeconds(15)))
            {
                manager.ApplyPreset(undo);
                Console.WriteLine("Reverted to previous layout.");
                return 1;
            }
            Console.WriteLine($"Applied preset '{preset.Name}'.");
            return 0;
        }

        case "hotkey":
        {
            // hotkey <preset> <keys|none>
            var config = store.Load();
            var preset = arg1 == null ? null : config.FindPreset(arg1);
            if (preset == null || args.Length < 3) { Console.Error.WriteLine("Usage: hotkey <preset> <keys|none>"); return 2; }
            if (!TryReadHotkey(args[2], out var hk)) return 2;
            preset.Hotkey = hk;
            store.Save(config);
            Console.WriteLine($"Preset '{preset.Name}' hotkey: {hk ?? "(none)"}");
            return 0;
        }

        case "toggle-hotkey":
        {
            // toggle-hotkey <n> <keys|none>
            var displays = manager.GetDisplays();
            if (!int.TryParse(arg1, out int n) || n < 1 || n > displays.Count || args.Length < 3)
            {
                Console.Error.WriteLine($"Usage: toggle-hotkey <1..{displays.Count}> <keys|none>");
                PrintDisplays(displays);
                return 2;
            }
            if (!TryReadHotkey(args[2], out var hk)) return 2;
            var config = store.Load();
            var d = displays[n - 1];
            if (hk == null) config.ToggleHotkeys.Remove(d.Key); else config.ToggleHotkeys[d.Key] = hk;
            store.Save(config);
            Console.WriteLine($"Toggle {d.FriendlyName} hotkey: {hk ?? "(none)"}");
            return 0;
        }

        case "panic-hotkey":
        {
            if (args.Length < 2 || !TryReadHotkey(args[1], out var hk)) { Console.Error.WriteLine("Usage: panic-hotkey <keys|none>"); return 2; }
            var config = store.Load();
            config.PanicHotkey = hk;
            store.Save(config);
            Console.WriteLine($"All-displays-on hotkey: {hk ?? "(none)"}");
            return 0;
        }

        default:
            Console.Error.WriteLine("Commands: list | toggle|on|off <n> | all-on | presets | save <name> | apply <name> [--yes] | delete <name>");
            Console.Error.WriteLine("          hotkey <preset> <keys|none> | toggle-hotkey <n> <keys|none> | panic-hotkey <keys|none>");
            return 2;
    }
}
catch (Exception ex) when (ex is DisplayConfigException or InvalidOperationException or IOException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

// Parses "Ctrl+Alt+1" (normalized) or "none" (null). Prints the error and returns false if invalid.
static bool TryReadHotkey(string text, out string? normalized)
{
    normalized = null;
    if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
    if (!Hotkey.TryParse(text, out var hk, out var error)) { Console.Error.WriteLine(error); return false; }
    normalized = hk.ToString();
    return true;
}

static bool WaitForConfirmation(TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        Console.Write($"\rKeep this layout? Press Y to keep, N to revert ({(int)(deadline - DateTime.UtcNow).TotalSeconds + 1}s) ");
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Y) { Console.WriteLine(); return true; }
            if (key == ConsoleKey.N) break;
        }
        Thread.Sleep(100);
    }
    Console.WriteLine();
    return false;
}

static void PrintDisplays(IReadOnlyList<DisplayInfo> displays)
{
    Console.WriteLine();
    for (int i = 0; i < displays.Count; i++)
    {
        var d = displays[i];
        var state = d.IsActive
            ? $"ON   {d.Width}x{d.Height} @{d.RefreshRate}Hz  pos ({d.PositionX},{d.PositionY}){(d.IsPrimary ? "  [primary]" : "")}  {d.GdiDeviceName}"
            : "off";
        Console.WriteLine($"  {i + 1}. {d.FriendlyName,-24} {state}");
    }
    Console.WriteLine();
}

static void PrintPreset(Preset p)
{
    Console.WriteLine($"  {p.Name}{(p.Hotkey != null ? $"  [{p.Hotkey}]" : "")}");
    foreach (var d in p.Displays)
    {
        var hz = d.RefreshDenominator == 0 ? 0 : Math.Round((double)d.RefreshNumerator / d.RefreshDenominator, 2);
        Console.WriteLine(d.Enabled
            ? $"      {d.FriendlyName,-24} ON   {d.Width}x{d.Height} @{hz}Hz  pos ({d.PositionX},{d.PositionY}){(d.IsPrimary ? "  [primary]" : "")}"
            : $"      {d.FriendlyName,-24} off");
    }
}
