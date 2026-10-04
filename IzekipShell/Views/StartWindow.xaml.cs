using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using IzekipShell.Interop;
using IzekipShell.Models;
using IzekipShell.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Windows.System;

namespace IzekipShell.Views;

public sealed partial class StartWindow : Window
{
    // Aramada uygulamalarin yaninda cikan ayar sayfalari.
    internal static readonly AppEntry[] SettingsPages =
    {
        Page("Ekran ayarları", "ms-settings:display", ""),
        Page("Ses ayarları", "ms-settings:sound", ""),
        Page("Bluetooth ve cihazlar", "ms-settings:bluetooth", ""),
        Page("Wi-Fi ve ağ", "ms-settings:network-wifi", ""),
        Page("Kişiselleştirme ve duvar kağıdı", "ms-settings:personalization-background", ""),
        Page("Renkler ve tema", "ms-settings:colors", ""),
        Page("Uygulamalar ve özellikler", "ms-settings:appsfeatures", ""),
        Page("Windows Update", "ms-settings:windowsupdate", ""),
        Page("Güç ve pil", "ms-settings:powersleep", ""),
        Page("Depolama", "ms-settings:storagesense", ""),
        Page("Bildirimler", "ms-settings:notifications", ""),
        Page("Fare", "ms-settings:mousetouchpad", ""),
        Page("Klavye ve dil", "ms-settings:regionlanguage", ""),
        Page("Tarih ve saat", "ms-settings:dateandtime", ""),
        Page("Hesaplar", "ms-settings:yourinfo", ""),
        Page("Gizlilik ve güvenlik", "ms-settings:privacy", ""),
        Page("Oyun", "ms-settings:gaming-gamebar", ""),
        Page("Görev Yöneticisi", "uri:taskmgr.exe", "", raw: true),
        Page("Denetim Masası", "uri:control.exe", "", raw: true),
        Page("Kayıt Defteri Düzenleyicisi", "uri:regedit.exe", "", raw: true),
    };

    static AppEntry Page(string name, string target, string glyph, bool raw = false) =>
        new() { Name = name, Id = raw ? target : "uri:" + target, Glyph = glyph, Subtitle = "Ayarlar" };

    static readonly string NoteFile = AppPaths.File("not.txt");

    readonly PopupController _popup;
    readonly ObservableCollection<AppEntry> _pinned = new(), _recent = new(), _frequent = new(), _allRecent = new();
    readonly CollectionViewSource _appsSource;
    readonly DispatcherQueueTimer _statsTimer, _noteTimer;
    string _tab = "home";

    public bool IsOpen => _popup.IsOpen;

    public StartWindow()
    {
        InitializeComponent();
        Title = "İzekip Başlat";
        _popup = new PopupController(this, Root);
        _popup.Opened += OnOpened;

        _appsSource = (CollectionViewSource)Root.Resources["AppsSource"];
        RecentGrid.ItemsSource = _recent;
        FrequentList.ItemsSource = _frequent;
        AllRecentList.ItemsSource = _allRecent;

        var user = Environment.UserName;
        UserPicture.DisplayName = user;

        _statsTimer = DispatcherQueue.CreateTimer();
        _statsTimer.Interval = TimeSpan.FromSeconds(1);
        _statsTimer.Tick += (_, _) =>
        {
            if (_popup.IsOpen) UpdateStats();
            else _statsTimer.Stop();
        };

        // Not yazma durunca kaydedilir.
        _noteTimer = DispatcherQueue.CreateTimer();
        _noteTimer.Interval = TimeSpan.FromMilliseconds(600);
        _noteTimer.IsRepeating = false;
        _noteTimer.Tick += (_, _) => SaveNote();
        try { if (File.Exists(NoteFile)) NoteBox.Text = File.ReadAllText(NoteFile); } catch { }
        NoteSaved.Text = "";

        BuildFolders();
        AppCatalog.Changed += Reload;
        ShellSettings.Changed += ReloadPins;
        AppUpdater.Changed += () => DispatcherQueue.TryEnqueue(UpdateUpdateCard);
        Reload();
    }

    double Scale => Root.XamlRoot?.RasterizationScale ?? 1;

    public void Toggle() => _popup.Toggle(() => App.Current.Taskbar.PopupRect(940, 680, right: false, gapDip: 0));

    public void Hide() => _popup.Hide();

    public void ForegroundChanged(IntPtr h) => _popup.ForegroundChanged(h);

    void OnOpened()
    {
        Greeting.Text = $"{GreetingFor(DateTime.Now.Hour)}, {Environment.UserName}";
        Today.Text = DateTime.Now.ToString("d MMMM yyyy, dddd", Format.Tr);
        SelectTab("home", animate: false);
        // Kartlar her acilista sirayla akarak gelsin.
        PinnedGrid.ItemsSource = null;
        PinnedGrid.ItemsSource = _pinned;
        LoadFrequent();
        LoadRecent();
        LoadClipboard();
        SystemStats.Cpu();
        UpdateStats();
        _statsTimer.Start();
        UpdateUpdateCard();
        Root.Focus(FocusState.Programmatic);
    }

    // ---- Kabugun kendi guncellemesi (OTA) ----

    void UpdateUpdateCard()
    {
        bool show = AppUpdater.State is AppUpdateState.Available or AppUpdateState.Downloading or AppUpdateState.ReadyToInstall;
        UpdateCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        UpdateProgress.Visibility = AppUpdater.State == AppUpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        if (AppUpdater.State == AppUpdateState.Downloading) UpdateProgress.Value = AppUpdater.DownloadPercent;

        switch (AppUpdater.State)
        {
            case AppUpdateState.Available:
                UpdateTitle.Text = $"Yeni sürüm hazır: v{AppUpdater.LatestVersion}";
                UpdateSubtitle.Text = string.IsNullOrWhiteSpace(AppUpdater.ReleaseNotes) ? "İzekip Shell için bir güncelleme var." : AppUpdater.ReleaseNotes;
                UpdateAction.Content = "Şimdi güncelle";
                UpdateAction.IsEnabled = true;
                break;
            case AppUpdateState.Downloading:
                UpdateTitle.Text = "İndiriliyor…";
                UpdateSubtitle.Text = $"%{AppUpdater.DownloadPercent} tamamlandı";
                UpdateAction.IsEnabled = false;
                break;
            case AppUpdateState.ReadyToInstall:
                UpdateTitle.Text = "Kurulmaya hazır";
                UpdateSubtitle.Text = "Kabuk kapanıp yeni sürümle yeniden açılacak.";
                UpdateAction.Content = "Yeniden başlat ve kur";
                UpdateAction.IsEnabled = true;
                break;
        }
    }

    void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (AppUpdater.State == AppUpdateState.Available) AppUpdater.DownloadAndInstallAsync();
        else if (AppUpdater.State == AppUpdateState.ReadyToInstall) AppUpdater.InstallAndRestart();
    }

    static string GreetingFor(int hour) => hour switch
    {
        < 5 => "İyi geceler",
        < 12 => "Günaydın",
        < 18 => "İyi günler",
        < 22 => "İyi akşamlar",
        _ => "İyi geceler",
    };

    void Reload()
    {
        ReloadPins();
        var groups = AppCatalog.Apps
            .GroupBy(a => a.Letter)
            .OrderBy(g => g.Key == "#" ? "" : g.Key, StringComparer.Create(Format.Tr, false))
            .Select(g => new AppGroup(g.Key, g))
            .ToList();
        _appsSource.Source = groups;
        AllList.ItemsSource = _appsSource.View;
    }

    void ReloadPins()
    {
        _pinned.Clear();
        foreach (var id in ShellSettings.Current.StartPins)
            if (AppCatalog.FindById(id) is { } app) _pinned.Add(app);
    }

    // En cok baslatilan bes uygulama (sabitlenenler disinda).
    void LoadFrequent()
    {
        var pins = ShellSettings.Current.StartPins;
        var top = ShellSettings.Current.LaunchCounts
            .Where(kv => !pins.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .Select(kv => AppCatalog.FindById(kv.Key))
            .OfType<AppEntry>()
            .Take(5)
            .ToList();
        _frequent.Clear();
        foreach (var app in top)
        {
            _frequent.Add(app);
            RequestIcon(app, 22);
        }
        FrequentSection.Visibility = _frequent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Windows'un "Son kullanilanlar" klasoru: her acilan dosya icin bir kisayol.
    async void LoadRecent()
    {
        var recent = await Task.Run(() =>
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            try
            {
                return new DirectoryInfo(dir).EnumerateFiles("*.lnk")
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(40)
                    .Select(f => (Link: f, Target: Shell.ShortcutTarget(f.FullName)))
                    .Where(x => x.Target is not null && File.Exists(x.Target))
                    .Take(20)
                    .Select(x => new AppEntry
                    {
                        Name = Path.GetFileName(x.Target!),
                        Id = "file:" + x.Link.FullName,
                        Glyph = "",
                        Subtitle = Ago.Of(x.Link.LastWriteTime),
                    })
                    .ToList();
            }
            catch { return new List<AppEntry>(); }
        });
        _recent.Clear();
        foreach (var r in recent.Take(4)) _recent.Add(r);
        _allRecent.Clear();
        foreach (var r in recent) _allRecent.Add(r);
        NoRecentText.Visibility = _recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void BuildFolders()
    {
        (string Name, string Glyph, string? Path)[] folders =
        {
            ("Masaüstü", "", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("İndirilenler", "", Shell.KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"))),
            ("Belgeler", "", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            ("Resimler", "", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            ("Müzik", "", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
            ("Videolar", "", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            ("Kullanıcı klasörü", "", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            ("Bu bilgisayar", "", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
        };
        foreach (var (name, glyph, path) in folders)
        {
            if (string.IsNullOrEmpty(path)) continue;
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["TaskButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 0, 14, 0),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 16, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"] },
                        new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            button.Click += (_, _) => { Hide(); AppCatalog.Open("explorer.exe", $"\"{path}\""); };
            FolderButtons.Children.Add(button);
        }
    }

    // ---- Sekmeler ----

    void Tab_Click(object sender, RoutedEventArgs e) => SelectTab((string)((FrameworkElement)sender).Tag, animate: true);

    void SelectTab(string tab, bool animate)
    {
        _tab = tab;
        HomeView.Visibility = tab == "home" ? Visibility.Visible : Visibility.Collapsed;
        AllList.Visibility = tab == "apps" ? Visibility.Visible : Visibility.Collapsed;
        FilesView.Visibility = tab == "files" ? Visibility.Visible : Visibility.Collapsed;

        int index = tab switch { "apps" => 1, "files" => 2, _ => 0 };
        double y = 33 + index * 54;
        if (animate)
        {
            var slide = new DoubleAnimation
            {
                To = y,
                Duration = TimeSpan.FromMilliseconds(320),
                EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 },
            };
            Storyboard.SetTarget(slide, IndicatorMove);
            Storyboard.SetTargetProperty(slide, "Y");
            var board = new Storyboard();
            board.Children.Add(slide);
            board.Begin();
            UIElement view = tab switch { "apps" => AllList, "files" => FilesView, _ => HomeView };
            WindowHelper.Entrance(view, 24);
        }
        else IndicatorMove.Y = y;
    }

    // ---- Widget'lar ----

    void UpdateStats()
    {
        int cpu = SystemStats.Cpu();
        CpuRing.Value = cpu;
        CpuText.Text = $"%{cpu}";

        var mem = SystemStats.Memory();
        RamRing.Value = mem.Percent;
        RamText.Text = $"%{mem.Percent}";
        ToolTipService.SetToolTip(RamRing, mem.Text);

        var disk = SystemStats.SystemDisk();
        DiskRing.Value = disk.Percent;
        DiskText.Text = $"%{disk.Percent}";
        DiskLabel.Text = $"{disk.Drive} diski";

        UptimeText.Text = "Bilgisayar " + SystemStats.Uptime();
    }

    void Note_TextChanged(object sender, TextChangedEventArgs e)
    {
        NoteSaved.Text = "yazılıyor…";
        _noteTimer.Stop();
        _noteTimer.Start();
    }

    void SaveNote()
    {
        try
        {
            File.WriteAllText(NoteFile, NoteBox.Text);
            NoteSaved.Text = "kaydedildi";
        }
        catch { NoteSaved.Text = "kaydedilemedi"; }
    }

    async void LoadClipboard()
    {
        try
        {
            var view = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                var text = (await view.GetTextAsync()).Trim();
                ClipboardText.Text = text.Length > 0 ? text.ReplaceLineEndings(" ") : "Boş";
            }
            else if (view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                ClipboardText.Text = "Dosyalar kopyalandı";
            else if (view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
                ClipboardText.Text = "Bir resim kopyalandı";
            else
                ClipboardText.Text = "Boş";
        }
        catch { ClipboardText.Text = "Okunamadı"; }
    }

    // ---- Girdiler ----

    void RequestIcon(AppEntry entry, double dip)
    {
        if (entry.IconRequested || entry.IconPath is not { } path) return;
        entry.IconRequested = true;
        IconCache.Instance!.Get(path, (int)Math.Round(dip * Scale), src => entry.Icon = src);
    }

    void Entries_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is AppEntry entry) RequestIcon(entry, sender == PinnedGrid ? 36 : 28);
    }

    void Entry_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppEntry entry) Launch(entry);
    }

    void Frequent_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppEntry entry) Launch(entry);
    }

    void Launch(AppEntry entry, bool admin = false)
    {
        Hide();
        AppCatalog.Launch(entry, admin);
    }

    void Entry_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not AppEntry entry) return;
        e.Handled = true;
        var menu = new MenuFlyout();
        menu.Items.Add(TaskbarWindow.Entry("Aç", "", () => Launch(entry)));
        if (entry.ExePath is not null)
            menu.Items.Add(TaskbarWindow.Entry("Yönetici olarak çalıştır", "", () => Launch(entry, admin: true)));
        if (entry.IsApp)
        {
            var s = ShellSettings.Current;
            bool onStart = s.StartPins.Contains(entry.Id), onBar = s.TaskbarPins.Contains(entry.Id);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(TaskbarWindow.Entry(onStart ? "Başlangıçtan kaldır" : "Başlangıca sabitle", onStart ? "" : "",
                () => ShellSettings.Toggle(s.StartPins, entry.Id)));
            menu.Items.Add(TaskbarWindow.Entry(onBar ? "Görev çubuğundan kaldır" : "Görev çubuğuna sabitle", onBar ? "" : "",
                () => ShellSettings.Toggle(s.TaskbarPins, entry.Id)));
        }
        if (entry.ExePath is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(TaskbarWindow.Entry("Dosya konumunu aç", "", () =>
            {
                Hide();
                AppCatalog.Open("explorer.exe", $"/select,\"{entry.ExePath}\"");
            }));
        }
        var target = (UIElement)sender;
        menu.ShowAt(target, e.GetPosition(target));
    }

    // Yazmaya baslayinca arama penceresi o harfle acilir.
    void Root_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
    {
        if (NoteBox.FocusState != FocusState.Unfocused || char.IsControl(args.Character) || char.IsWhiteSpace(args.Character)) return;
        args.Handled = true;
        OpenSearch(args.Character.ToString());
    }

    void OpenSearch(string text)
    {
        Hide();
        App.Current.Search.Open(text);
    }

    void Search_Click(object sender, RoutedEventArgs e) => OpenSearch("");

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (_tab != "home") SelectTab("home", animate: true);
        else Hide();
        e.Handled = true;
    }

    void User_Tapped(object sender, TappedRoutedEventArgs e)
    {
        Hide();
        AppCatalog.Open("ms-settings:yourinfo");
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        AppCatalog.Open("ms-settings:");
    }

    // ---- Guc ----

    void Lock_Click(object sender, RoutedEventArgs e) { Hide(); Native.LockWorkStation(); }
    void SignOut_Click(object sender, RoutedEventArgs e) { App.Current.Quit(); Native.ExitWindowsEx(0, 0); }
    void Sleep_Click(object sender, RoutedEventArgs e) { Hide(); Native.SetSuspendState(false, false, false); }
    void Restart_Click(object sender, RoutedEventArgs e) => PowerOff("/r");
    void Shutdown_Click(object sender, RoutedEventArgs e) => PowerOff("/s");
    void Quit_Click(object sender, RoutedEventArgs e) => App.Current.Quit();

    // Kapanistan once Windows arayuzu geri yuklenir; bir sonraki acilis temiz olsun.
    static void PowerOff(string flag)
    {
        var info = new ProcessStartInfo("shutdown.exe", $"{flag} /t 0") { CreateNoWindow = true, UseShellExecute = false };
        App.Current.Quit();
        Process.Start(info);
    }
}
