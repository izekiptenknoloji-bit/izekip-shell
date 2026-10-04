using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IzekipShell.Services;

public enum AppUpdateState { Unknown, Checking, UpToDate, Available, Downloading, ReadyToInstall, Installing, Error }

// Kabugun kendi OTA guncellemesi: GitHub Releases'teki en son surumu denetler, bir .zip
// varligini indirir, acar ve calisan .exe'nin yerine kopyalayip kendini yeniden baslatir.
// Hicbir surum yayinlanmamissa ya da internet yoksa sessizce "UpToDate" sayilir.
public static class AppUpdater
{
    public const string CurrentVersion = "1.2.3";
    const string Owner = "izekiptenknoloji-bit", Repo = "izekip-shell";

    public static AppUpdateState State { get; private set; } = AppUpdateState.Unknown;
    public static string? LatestVersion { get; private set; }
    public static string? ReleaseNotes { get; private set; }
    public static string? ReleaseUrl { get; private set; }
    public static int DownloadPercent { get; private set; }
    public static string? Error { get; private set; }
    public static DateTime? AutoInstallAt { get; private set; }
    public static event Action? Changed;

    static string? _assetUrl;
    static bool _busy;
    static Timer? _installTimer;

    // Kurulumdan once kullanici fark etsin ve isterse ertelesin diye kisa bir bekleme.
    static readonly TimeSpan AutoInstallDelay = TimeSpan.FromSeconds(20);

    public static string Summary => State switch
    {
        AppUpdateState.Checking => "Güncelleme denetleniyor…",
        AppUpdateState.Available => $"Yeni sürüm bulundu: v{LatestVersion}",
        AppUpdateState.Downloading => $"İndiriliyor… %{DownloadPercent}",
        AppUpdateState.ReadyToInstall => "Otomatik kuruluma hazırlanıyor",
        AppUpdateState.Installing => "Kuruluyor…",
        AppUpdateState.Error => "Denetlenemedi",
        AppUpdateState.UpToDate => "En güncel sürümdesin",
        _ => "",
    };

    public static TimeSpan? AutoInstallIn => AutoInstallAt is { } at && at > DateTime.Now ? at - DateTime.Now : null;

    public static async void CheckAsync()
    {
        if (_busy) return;
        _busy = true;
        State = AppUpdateState.Checking;
        Changed?.Invoke();
        try
        {
            using var http = Http();
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            ReleaseNotes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
            ReleaseUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            _assetUrl = null;
            if (root.TryGetProperty("assets", out var assets))
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        _assetUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

            bool newer = Version.TryParse(tag, out var latest) && Version.TryParse(CurrentVersion, out var current) && latest > current;
            LatestVersion = tag;
            State = newer && _assetUrl is not null ? AppUpdateState.Available : AppUpdateState.UpToDate;
        }
        catch (Exception e)
        {
            Error = e.Message;
            State = AppUpdateState.Error;
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
            // Bulununca elle onay beklemeden indirip hazirlanir; kurulum yine de kisa bir
            // geri sayimla yapilir ki kullanici isterse ertelesin.
            if (State == AppUpdateState.Available) DownloadAndInstallAsync();
        }
    }

    public static async void DownloadAndInstallAsync()
    {
        if (_busy || _assetUrl is null) return;
        _busy = true;
        State = AppUpdateState.Downloading;
        DownloadPercent = 0;
        Changed?.Invoke();
        try
        {
            var work = AppPaths.File("guncelleme");
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            Directory.CreateDirectory(work);
            var zipPath = Path.Combine(work, "surum.zip");
            var extractPath = Path.Combine(work, "cikarilan");

            using (var http = Http())
            using (var response = await http.GetAsync(_assetUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var dest = File.Create(zipPath);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, n));
                    read += n;
                    if (total > 0)
                    {
                        DownloadPercent = (int)(100 * read / total);
                        Changed?.Invoke();
                    }
                }
            }

            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
            // Zip tek bir alt klasor iceriyorsa (GitHub'in otomatik kaynak arsivleri gibi), onun icine in.
            var entries = Directory.GetFileSystemEntries(extractPath);
            var root = entries.Length == 1 && Directory.Exists(entries[0]) && !File.Exists(Path.Combine(extractPath, "IzekipShell.exe"))
                ? entries[0] : extractPath;
            if (!File.Exists(Path.Combine(root, "IzekipShell.exe")))
                throw new FileNotFoundException("Paket içinde IzekipShell.exe bulunamadı.");

            _stagedRoot = root;
            State = AppUpdateState.ReadyToInstall;
            ScheduleAutoInstall(AutoInstallDelay);
        }
        catch (Exception e)
        {
            Error = e.Message;
            State = AppUpdateState.Error;
        }
        finally
        {
            _busy = false;
            Changed?.Invoke();
        }
    }

    static string? _stagedRoot;

    static void ScheduleAutoInstall(TimeSpan delay)
    {
        AutoInstallAt = DateTime.Now + delay;
        _installTimer?.Dispose();
        _installTimer = new Timer(_ => App.Current.UI.TryEnqueue(InstallAndRestart), null, delay, Timeout.InfiniteTimeSpan);
    }

    // Kurulumu bir saat erteler (ustteki kartta "Ertele" dugmesi).
    public static void Postpone()
    {
        if (State != AppUpdateState.ReadyToInstall) return;
        ScheduleAutoInstall(TimeSpan.FromHours(1));
        Changed?.Invoke();
    }

    // Guncel dosyalari calisan klasorun uzerine kopyalayan bir betik yazar, kabugu kapatir,
    // betigi baslatir (kabuk tamamen cikinca dosyalar serbest kalir) ve exe'yi yeniden acar.
    public static void InstallAndRestart()
    {
        if (_stagedRoot is null) return;
        _installTimer?.Dispose();
        _installTimer = null;
        AutoInstallAt = null;
        State = AppUpdateState.Installing;
        Changed?.Invoke();

        var installDir = AppContext.BaseDirectory.TrimEnd('\\');
        var exePath = Path.Combine(installDir, "IzekipShell.exe");
        var scriptPath = AppPaths.File("guncelle.cmd");
        File.WriteAllText(scriptPath, $"""
            @echo off
            :bekle
            tasklist /fi "imagename eq IzekipShell.exe" | find /i "IzekipShell.exe" >nul
            if not errorlevel 1 (
                timeout /t 1 >nul
                goto bekle
            )
            robocopy "{_stagedRoot}" "{installDir}" /E /IS /IT /R:5 /W:1 /NFL /NDL /NJH /NJS
            start "" "{exePath}"
            del "%~f0"
            """);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        App.Current.Quit();
    }

    static HttpClient Http()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IzekipShell", CurrentVersion));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}
