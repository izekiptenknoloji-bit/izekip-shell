using System.Text.Json;

namespace IzekipShell.Services;

public static class AppPaths
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IzekipShell");

    static AppPaths() => Directory.CreateDirectory(Data);

    public static string File(string name) => Path.Combine(Data, name);

    public static void Log(Exception e)
    {
        try { System.IO.File.AppendAllText(File("hata.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e}\n\n"); }
        catch { }
    }
}

// Sabitlenen uygulamalar: Baslat ve gorev cubugu. Kimlikler shell:AppsFolder kimlikleridir.
public sealed class ShellSettings
{
    public List<string> StartPins { get; set; } = new();
    public List<string> TaskbarPins { get; set; } = new();
    public bool PinsInitialized { get; set; }
    // Uygulama kimligi -> kac kez baslatildi ("Sik kullandiklarin").
    public Dictionary<string, int> LaunchCounts { get; set; } = new();

    static readonly string FilePath = AppPaths.File("ayarlar.json");
    public static ShellSettings Current { get; } = Load();
    public static event Action? Changed;

    static ShellSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
                return JsonSerializer.Deserialize<ShellSettings>(System.IO.File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try { System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { AppPaths.Log(e); }
        Changed?.Invoke();
    }

    public static void Toggle(List<string> list, string id)
    {
        if (!list.Remove(id)) list.Add(id);
        Current.Save();
    }
}
