using System.Text.Json;
using IzekipShell.Interop;
using Microsoft.Win32;

namespace IzekipShell.Services;

// Kabukla gelen imlec seti (Tools/make_cursors.py uretir). Kabuk acikken etkin olur;
// onceki imlecler diske yedeklenir ve cikista (ya da coktuyse bir sonraki acilista) geri yazilir.
public static class CursorTheme
{
    sealed record Saved(string Name, string? Value, RegistryValueKind Kind);

    const string KeyPath = @"Control Panel\Cursors";
    static readonly string BackupFile = AppPaths.File("imlecler.json");
    static readonly object Gate = new();

    static readonly (string Name, string File)[] Map =
    {
        ("Arrow", "arrow.cur"), ("Hand", "hand.cur"), ("IBeam", "ibeam.cur"), ("Crosshair", "cross.cur"),
        ("SizeWE", "size_we.cur"), ("SizeNS", "size_ns.cur"), ("SizeNWSE", "size_nwse.cur"), ("SizeNESW", "size_nesw.cur"),
        ("SizeAll", "move.cur"), ("No", "no.cur"), ("UpArrow", "up.cur"), ("Help", "help.cur"),
        ("Wait", "busy.ani"), ("AppStarting", "working.ani"),
    };

    public static void Apply()
    {
        lock (Gate)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Cursors");
            if (!Directory.Exists(dir)) return;
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null) return;

            // Yedek yalniz bir kez alinir: coken bir oturumun yedegi ezilmesin.
            if (!File.Exists(BackupFile))
            {
                var saved = Map.Select(m => m.Name).Append("")
                    .Select(n => new Saved(n, key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string,
                        key.GetValue(n) is null ? RegistryValueKind.String : key.GetValueKind(n)))
                    .ToList();
                File.WriteAllText(BackupFile, JsonSerializer.Serialize(saved));
            }

            foreach (var (name, file) in Map)
            {
                var path = Path.Combine(dir, file);
                if (File.Exists(path)) key.SetValue(name, path, RegistryValueKind.String);
            }
            key.SetValue("", "İzekip", RegistryValueKind.String);
            Reload();
        }
    }

    public static void Restore()
    {
        lock (Gate)
        {
            if (!File.Exists(BackupFile)) return;
            try
            {
                var saved = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(BackupFile)) ?? new();
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
                if (key is null) return;
                foreach (var s in saved)
                {
                    if (s.Value is null) key.DeleteValue(s.Name, throwOnMissingValue: false);
                    else key.SetValue(s.Name, s.Value, s.Kind == RegistryValueKind.ExpandString ? RegistryValueKind.ExpandString : RegistryValueKind.String);
                }
                Reload();
                File.Delete(BackupFile);
            }
            catch (Exception e) { AppPaths.Log(e); }
        }
    }

    static void Reload() =>
        Native.SystemParametersInfoPtr(0x57 /* SPI_SETCURSORS */, 0, 0, 0x3 /* SPIF_UPDATEINIFILE | SPIF_SENDCHANGE */);
}
