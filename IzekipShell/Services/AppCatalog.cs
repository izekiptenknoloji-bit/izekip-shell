using System.ComponentModel;
using System.Diagnostics;
using IzekipShell.Models;
using Microsoft.UI.Dispatching;

namespace IzekipShell.Services;

// Yuklu uygulamalar: Windows Baslat menusunun kendi listesi olan shell:AppsFolder'dan okunur.
// Hem masaustu (kisayol) hem Store uygulamalari gelir; ikisi de ayni yolla baslatilir.
public static class AppCatalog
{
    public static List<AppEntry> Apps { get; private set; } = new();
    public static event Action? Changed;

    static readonly string[] SkipWords = { "uninstall", "kaldır", "readme", "benioku", "help", "yardım" };
    static readonly string[] SkipExtensions = { ".url", ".txt", ".chm", ".pdf", ".html", ".htm", ".rtf" };

    public static async Task LoadAsync()
    {
        var ui = DispatcherQueue.GetForCurrentThread();
        var done = new TaskCompletionSource<List<AppEntry>>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(Enumerate()); }
            catch (Exception e) { AppPaths.Log(e); done.SetResult(new List<AppEntry>()); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        var apps = await done.Task;
        Apps = apps.OrderBy(a => a.Name, StringComparer.Create(Format.Tr, true)).ToList();
        EnsureDefaultPins();
        Changed?.Invoke();
    }

    static List<AppEntry> Enumerate()
    {
        var list = new List<AppEntry>();
        var type = Type.GetTypeFromProgID("Shell.Application")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        dynamic items = folder.Items();
        int count = items.Count;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            try
            {
                dynamic item = items.Item(i);
                string name = item.Name;
                string id = item.Path;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
                var lower = name.ToLower(Format.Tr);
                if (SkipWords.Any(lower.Contains)) continue;
                if (SkipExtensions.Any(e => id.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(new AppEntry { Name = name, Id = id, ExePath = ResolveExe(id) });
            }
            catch { }
        }
        return list;
    }

    // Masaustu uygulamalarinin kimligi "{bilinen klasor}\yol\uygulama.exe" bicimindedir.
    static string? ResolveExe(string id)
    {
        var path = id;
        if (path.StartsWith('{'))
        {
            int end = path.IndexOf('}');
            if (end > 0 && Guid.TryParse(path[1..end], out var folder) && Shell.KnownFolder(folder) is { } root)
                path = root + path[(end + 1)..];
        }
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(path) && File.Exists(path) ? path : null;
    }

    public static AppEntry? FindById(string id) =>
        Apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

    public static AppEntry? Find(string? aumid, string? exe) =>
        (aumid is null ? null : FindById(aumid)) ??
        (exe is null ? null : Apps.FirstOrDefault(a => string.Equals(a.ExePath, exe, StringComparison.OrdinalIgnoreCase)));

    // Ilk acilista Baslat ve gorev cubugu bos kalmasin.
    static void EnsureDefaultPins()
    {
        var s = ShellSettings.Current;
        if (s.PinsInitialized || Apps.Count == 0) return;

        string[][] start =
        {
            new[] { "Dosya Gezgini", "File Explorer" }, new[] { "Ayarlar", "Settings" }, new[] { "Microsoft Edge" },
            new[] { "Google Chrome" }, new[] { "Terminal", "Windows Terminal" }, new[] { "Not Defteri", "Notepad" },
            new[] { "Hesap Makinesi", "Calculator" }, new[] { "Microsoft Store" }, new[] { "Fotoğraflar", "Photos" },
            new[] { "Paint" }, new[] { "Roblox Studio" }, new[] { "Visual Studio 2026", "Visual Studio 2022" },
            new[] { "PyCharm" }, new[] { "Discord" }, new[] { "Steam" }, new[] { "Spotify" }, new[] { "Görev Yöneticisi", "Task Manager" },
        };
        string[][] taskbar =
        {
            new[] { "Dosya Gezgini", "File Explorer" }, new[] { "Microsoft Edge" }, new[] { "Google Chrome" }, new[] { "Terminal", "Windows Terminal" },
        };
        s.StartPins = Match(start);
        s.TaskbarPins = Match(taskbar);
        s.PinsInitialized = true;
        s.Save();
    }

    static List<string> Match(string[][] wanted)
    {
        var ids = new List<string>();
        foreach (var names in wanted)
        {
            var app = names.Select(n => Apps.FirstOrDefault(a => a.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase))).FirstOrDefault(a => a is not null)
                      ?? names.Select(n => Apps.FirstOrDefault(a => a.Name.StartsWith(n, StringComparison.CurrentCultureIgnoreCase))).FirstOrDefault(a => a is not null);
            if (app is not null && !ids.Contains(app.Id)) ids.Add(app.Id);
        }
        return ids;
    }

    // ---- Baslatma ----

    public static void Launch(AppEntry app, bool admin = false)
    {
        if (app.IsApp)
        {
            var counts = ShellSettings.Current.LaunchCounts;
            counts[app.Id] = counts.GetValueOrDefault(app.Id) + 1;
            ShellSettings.Current.Save();
        }
        try
        {
            if (admin && app.ExePath is not null)
                Run(new ProcessStartInfo(app.ExePath) { UseShellExecute = true, Verb = "runas" });
            else if (app.Id.StartsWith("uri:") || app.Id.StartsWith("file:"))
                Open(app.Id[(app.Id.IndexOf(':') + 1)..]);
            else if (app.Id.StartsWith("run:"))
                RunCommand(app.Id[4..]);
            else
                Open(@"shell:AppsFolder\" + app.Id);
        }
        catch (Exception e) { AppPaths.Log(e); }
    }

    public static void Open(string target, string? args = null)
    {
        try { Run(new ProcessStartInfo(target) { UseShellExecute = true, Arguments = args ?? "" }); }
        catch (Exception e) { AppPaths.Log(e); }
    }

    // "cmd /k dir" gibi: once tamami, olmazsa ilk kelime program, gerisi arguman.
    public static void RunCommand(string text)
    {
        text = Environment.ExpandEnvironmentVariables(text.Trim());
        try { Run(new ProcessStartInfo(text) { UseShellExecute = true }); return; }
        catch (Win32Exception) { }
        int space = text.IndexOf(' ');
        if (space > 0)
        {
            try { Run(new ProcessStartInfo(text[..space]) { UseShellExecute = true, Arguments = text[(space + 1)..] }); }
            catch (Exception e) { AppPaths.Log(e); }
        }
    }

    static void Run(ProcessStartInfo info)
    {
        try { using var _ = Process.Start(info); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { } // kullanici UAC'yi iptal etti
    }
}
