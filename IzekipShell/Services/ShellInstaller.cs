using Microsoft.Win32;

namespace IzekipShell.Services;

// Explorer33'u Windows'un gercek oturum kabugu yapar: HKCU duzeyinde Winlogon\Shell
// degerini kendi exe'mize cevirir. HKLM'ye dokunmaz, yonetici hakki gerekmez, ve sadece
// bu kullaniciyi etkiler - diger hesaplar yine explorer.exe ile acilir.
public static class ShellInstaller
{
    const string KeyPath = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
    static readonly string BackupFile = AppPaths.File("onceki_kabuk.txt");

    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                var value = key?.GetValue("Shell") as string;
                return value is not null && string.Equals(value, ExePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }

    static string ExePath => Path.Combine(AppContext.BaseDirectory, "IzekipShell.exe");

    // Mevcut Shell degerini yedekler (yoksa "yoktu" diye isaretler) ve kendi exe'mizi yazar.
    public static void Install()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (!File.Exists(BackupFile))
        {
            var previous = key.GetValue("Shell") as string;
            File.WriteAllText(BackupFile, previous ?? "");
        }
        key.SetValue("Shell", ExePath, RegistryValueKind.String);
        BootGuard.Reset();
    }

    // Yedekten geri yukler; yedek dosyasi yoksa (daha once hic kurulmamissa) degeri siler,
    // Windows kendi varsayilanina (explorer.exe) doner.
    public static void Uninstall()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (File.Exists(BackupFile))
            {
                var previous = File.ReadAllText(BackupFile);
                if (string.IsNullOrEmpty(previous)) key.DeleteValue("Shell", throwOnMissingValue: false);
                else key.SetValue("Shell", previous, RegistryValueKind.String);
                File.Delete(BackupFile);
            }
            else key.DeleteValue("Shell", throwOnMissingValue: false);
        }
        catch (Exception e) { AppPaths.Log(e); }
    }
}

// Gercek kabuk modunda acilista coken bir surumun kullaniciyi masaustusuz birakmamasi icin
// guvenlik agi: art arda basarisiz (kararli acilisa ulasamayan) birkac denemeden sonra
// otomatik olarak explorer.exe'ye geri doner.
public static class BootGuard
{
    // Sunucu kapanmasinin/winlogon'un her basarisiz girisi oturumu kapatmasina yol acmasi
    // ihtimaline karsi, ilk basarisiz denemeden sonra hemen geri doner (ikinci kotu girisi beklemez).
    const int MaxFailures = 1;
    static readonly string StateFile = AppPaths.File("acilis_durumu.json");

    public sealed class State
    {
        public bool DenemeDevamEdiyor { get; set; }
        public int BasarisizSayisi { get; set; }
    }

    // true: normal acilista devam et. false: cok fazla ust uste basarisizlik var,
    // cagiran taraf gercek kabugu kaldirip duz explorer.exe baslatmali.
    public static bool BeginAttempt()
    {
        var state = Load();
        if (state.DenemeDevamEdiyor)
        {
            state.BasarisizSayisi++;
            if (state.BasarisizSayisi >= MaxFailures)
            {
                Save(new State());
                return false;
            }
        }
        state.DenemeDevamEdiyor = true;
        Save(state);
        return true;
    }

    // Acilis gercekten kararli oldugunda (ornegin 15 saniye sorunsuz calistiktan sonra) cagrilir.
    public static void MarkStable() => Save(new State());

    public static void Reset() => Save(new State());

    static State Load()
    {
        try
        {
            if (File.Exists(StateFile))
                return System.Text.Json.JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) ?? new State();
        }
        catch { }
        return new State();
    }

    static void Save(State state)
    {
        try { File.WriteAllText(StateFile, System.Text.Json.JsonSerializer.Serialize(state)); }
        catch { }
    }
}
