using System.Diagnostics;
using IzekipShell.Services;
using IzekipShell.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace IzekipShell;

// Test modu kabuk: explorer.exe'nin gorev cubugu ve masaustu gizlenir, yerine bizimkiler gelir.
// Kapatinca (Baslat > Guc > Kabuktan cik ya da "IzekipShell.exe --kapat") her sey geri alinir.
public partial class App : Application
{
    const string QuitEvent = "IzekipShell.Kapat";

    public static new App Current => (App)Application.Current;

    public DispatcherQueue UI { get; private set; } = null!;
    // Son tiklanan gorev cubugu; acilan paneller onun ekranina yerlesir.
    public TaskbarWindow Taskbar { get; set; } = null!;
    public List<TaskbarWindow> Taskbars { get; } = new();
    public StartWindow StartMenu { get; private set; } = null!;
    public PanelWindow Panel { get; private set; } = null!;
    public SearchWindow Search { get; private set; } = null!;
    public ClockWindow Clock { get; private set; } = null!;
    public List<DesktopWindow> Desktops { get; } = new();

    Mutex? _single;
    bool _quitting;

    public App()
    {
        // Kabuk tek bir hata yuzunden kapanmasin; kayda yazip devam eder.
        UnhandledException += (_, e) =>
        {
            AppPaths.Log(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { ExplorerHider.Restore(); CursorTheme.Restore(); };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var rawArgv = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var argv = rawArgv.Select(a => a.ToLowerInvariant()).ToArray();
        if (argv.Contains("--kapat"))
        {
            if (EventWaitHandle.TryOpenExisting(QuitEvent, out var quit)) quit.Set();
            Exit();
            return;
        }
        if (argv.Contains("--geri-yukle"))
        {
            ExplorerHider.RestoreStale();
            CursorTheme.Restore();
            Exit();
            return;
        }
        if (argv.Length >= 3 && argv[0] == "--guncelleme-tamamla")
        {
            RunUpdateHelper(rawArgv[1], rawArgv[2]);
            return;
        }
        if (argv.Contains("--kur-kabuk")) { ShellInstaller.Install(); Exit(); return; }
        if (argv.Contains("--kabuk-kaldir")) { ShellInstaller.Uninstall(); Exit(); return; }

        // Gercek oturum kabugu olarak kuruluysa: art arda basarisiz acilista guvenlik agi
        // devreye girer, explorer.exe'ye geri doner. Boylece bozuk bir surum kullaniciyi
        // masaustusuz birakmaz.
        IsRealShell = ShellInstaller.IsInstalled;
        if (IsRealShell && !BootGuard.BeginAttempt())
        {
            ShellInstaller.Uninstall();
            try { Process.Start("explorer.exe"); } catch (Exception e) { AppPaths.Log(e); }
            Exit();
            return;
        }

        _single = new Mutex(true, "IzekipShell.TekKopya", out bool first);
        if (!first) { Exit(); return; }

        try { Boot(); }
        catch (Exception e)
        {
            AppPaths.Log(e);
            Quit();
        }
    }

    public bool IsRealShell { get; private set; }
    DispatcherQueueTimer? _stableTimer; // alan olarak tutulur: yerel degisken GC tarafindan erken toplanip Tick hic ateslenmeyebilir.

    // OTA guncellemesi: eski surum tamamen kapanana kadar tek basina "Guncelleniyor" ekranini
    // gosterir, sonra dosyalari kopyalayip yeni surumu acar. Tam kabuk burada hic calismaz.
    void RunUpdateHelper(string stagedRoot, string installDir)
    {
        UI = DispatcherQueue.GetForCurrentThread();
        var win = new UpdatingWindow();

        var worker = new Thread(() =>
        {
            try
            {
                int myPid = Environment.ProcessId;
                while (Process.GetProcessesByName("IzekipShell").Any(p => p.Id != myPid))
                    Thread.Sleep(300);

                win.SetStatus("Dosyalar kopyalanıyor…");
                var robocopy = Process.Start(new ProcessStartInfo("robocopy.exe",
                    $"\"{stagedRoot}\" \"{installDir}\" /E /IS /IT /R:5 /W:1 /NFL /NDL /NJH /NJS")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                robocopy?.WaitForExit();

                win.SetStatus("Yeniden başlatılıyor…");
                Process.Start(new ProcessStartInfo(Path.Combine(installDir, "IzekipShell.exe")) { UseShellExecute = true });
                Thread.Sleep(400);
            }
            catch (Exception e) { AppPaths.Log(e); }
            finally { UI.TryEnqueue(Exit); }
        }) { IsBackground = true };
        worker.Start();
    }

    void Boot()
    {
        UI = DispatcherQueue.GetForCurrentThread();
        // Gercek kabuk modunda Windows explorer.exe'yi kendiliginden baslatmaz (biz onun
        // yerine gecmisizdir); masaustu simgeleri/bildirim altyapisi icin kendimiz baslatiriz,
        // sonra her zamanki gibi gorev cubugunu/penceresini gizleriz.
        if (IsRealShell) SpawnExplorerForDesktop();
        ExplorerHider.RestoreStale();
        IconCache.Instance = new IconCache(UI);
        ExplorerHider.Hide(UI);
        CursorTheme.Apply();

        var displays = Displays();
        foreach (var area in displays)
        {
            var desktop = new DesktopWindow(area.OuterBounds, area.IsPrimary);
            Desktops.Add(desktop);
            desktop.ShowDesktop();
        }

        foreach (var area in displays.OrderByDescending(a => a.IsPrimary))
        {
            var bar = new TaskbarWindow(area.OuterBounds);
            Taskbars.Add(bar);
            bar.ShowBar();
        }
        Taskbar = Taskbars[0];
        SystemTheme.Watch(WindowHelper.Handle(Taskbar));
        StartMenu = new StartWindow();
        Panel = new PanelWindow();
        Search = new SearchWindow();
        Clock = new ClockWindow();

        WinKeyHook.Install(
            onStart: () => UI.TryEnqueue(() => { Taskbar = BarUnderCursor(); StartMenu.Toggle(); }),
            onDesktop: () => UI.TryEnqueue(WindowTracker.ToggleDesktop),
            onSearch: () => UI.TryEnqueue(() => { Taskbar = BarUnderCursor(); Search.Toggle(); }));

        _ = AppCatalog.LoadAsync();
        ListenForQuit();
        StartUpdateChecks();

        // Gercek kabuk modunda: sorunsuz 15 saniye gecince "kararli acilis" olarak isaretlenir,
        // guvenlik agindaki basarisizlik sayaci sifirlanir.
        if (IsRealShell)
        {
            _stableTimer = UI.CreateTimer();
            _stableTimer.Interval = TimeSpan.FromSeconds(15);
            _stableTimer.IsRepeating = false;
            _stableTimer.Tick += (_, _) => BootGuard.MarkStable();
            _stableTimer.Start();
        }
    }

    // Gercek kabuk modunda masaustu/bildirim altyapisi icin explorer.exe'yi kendimiz baslatir,
    // gorev cubugu penceresi belirene kadar (en fazla ~5sn) kisaca bekleriz.
    static void SpawnExplorerForDesktop()
    {
        try
        {
            Process.Start("explorer.exe");
            for (int i = 0; i < 50 && Interop.Native.FindWindow("Shell_TrayWnd", null) == 0; i++)
                Thread.Sleep(100);
        }
        catch (Exception e) { AppPaths.Log(e); }
    }

    // FindAll'in donen listesi foreach ile gezilince bazi surumlerde patliyor; dizinle gezilir.
    static List<DisplayArea> Displays()
    {
        var list = new List<DisplayArea>();
        try
        {
            var all = DisplayArea.FindAll();
            for (int i = 0; i < all.Count; i++) list.Add(all[i]);
        }
        catch { }
        if (list.Count == 0) list.Add(DisplayArea.Primary);
        return list;
    }

    // Klavyeyle acilan Baslat/arama, farenin bulundugu ekranda acilir.
    TaskbarWindow BarUnderCursor()
    {
        Interop.Native.GetCursorPos(out var p);
        return Taskbars.FirstOrDefault(b => p.X >= b.Monitor.X && p.X < b.Monitor.X + b.Monitor.Width
                                         && p.Y >= b.Monitor.Y && p.Y < b.Monitor.Y + b.Monitor.Height) ?? Taskbars[0];
    }

    // Ilk denetim hemen, sonra her birkac saatte bir tekrar edilir.
    void StartUpdateChecks()
    {
        var timer = UI.CreateTimer();
        timer.Interval = TimeSpan.FromHours(3);
        timer.Tick += (_, _) => WindowsUpdate.RefreshAsync();
        timer.Start();

        // Kabugun kendi surumu: Windows Update'ten bagimsiz, GitHub Releases'ten.
        AppUpdater.CheckAsync();
        var appTimer = UI.CreateTimer();
        appTimer.Interval = TimeSpan.FromHours(6);
        appTimer.Tick += (_, _) => AppUpdater.CheckAsync();
        appTimer.Start();
    }

    public bool IsDesktopWindow(IntPtr h) => Desktops.Any(d => d.Hwnd == h);

    void ListenForQuit()
    {
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEvent);
        var thread = new Thread(() =>
        {
            quit.WaitOne();
            UI.TryEnqueue(Quit);
        }) { IsBackground = true };
        thread.Start();
    }

    public void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        WinKeyHook.Uninstall();
        foreach (var bar in Taskbars)
        {
            try { bar.Unregister(); } catch (Exception e) { AppPaths.Log(e); }
        }
        ExplorerHider.Restore();
        CursorTheme.Restore();
        Exit();
        // Gizli pencereler surecin kapanmasini geciktirebiliyor; Windows arayuzu zaten geri geldi.
        new Thread(() => { Thread.Sleep(1500); Environment.Exit(0); }) { IsBackground = true }.Start();
    }
}
