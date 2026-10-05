using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using IzekipShell.Interop;
using IzekipShell.Models;
using IzekipShell.Services;
using Microsoft.UI.Dispatching;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace IzekipShell.Views;

// Her ekran icin bir masaustu: duvar kagidi ve (ana ekranda) masaustu simgeleri.
// Pencere hep en altta kalir; tiklansa da diger pencerelerin ustune cikmaz.
public sealed partial class DesktopWindow : Window
{
    const string ThisPc = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    readonly RectInt32 _bounds;
    readonly bool _primary;
    readonly ObservableCollection<DesktopItem> _items = new();
    readonly List<FileSystemWatcher> _watchers = new();
    readonly DispatcherQueueTimer _reload;

    public IntPtr Hwnd { get; }

    public DesktopWindow(RectInt32 bounds, bool primary)
    {
        InitializeComponent();
        SystemTheme.Bind(Root);
        Title = "Explorer33 Masaüstü";
        _bounds = bounds;
        _primary = primary;
        Hwnd = WindowHelper.Handle(this);
        WindowHelper.MakeChrome(this, topmost: false);
        WindowHelper.Subclass(Hwnd, KeepAtBottom);
        Icons.ItemsSource = _items;

        _reload = DispatcherQueue.CreateTimer();
        _reload.Interval = TimeSpan.FromMilliseconds(400);
        _reload.IsRepeating = false;
        _reload.Tick += (_, _) => LoadItems();

        // DWM bazen odak/tema degisince cerceveyi geri ekliyor; ucuz oldugu icin periyodik silinir.
        var borderTimer = DispatcherQueue.CreateTimer();
        borderTimer.Interval = TimeSpan.FromSeconds(2);
        borderTimer.Tick += (_, _) => { WindowHelper.StripFrame(Hwnd); WindowHelper.NoBorder(Hwnd); };
        borderTimer.Start();

        if (!primary) Icons.Visibility = Visibility.Collapsed;
        Root.RightTapped += (_, e) => { if (!e.Handled) ShowBackgroundMenu(e.GetPosition(Root)); };
    }

    public void ShowDesktop()
    {
        // Farkli DPI'li ekrana tasininca Windows boyutu olceklendirir; ikinci kez yerlestir.
        AppWindow.MoveAndResize(_bounds);
        AppWindow.MoveAndResize(_bounds);
        Activate();
        AppWindow.MoveAndResize(_bounds);
        Native.SetWindowPos(Hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        LoadWallpaper();
        if (_primary)
        {
            LoadItems();
            Watch();
        }
    }

    static IntPtr? KeepAtBottom(uint msg, IntPtr w, IntPtr l)
    {
        if (msg != Native.WM_WINDOWPOSCHANGING) return null;
        var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(l);
        if ((pos.flags & Native.SWP_NOZORDER) == 0)
        {
            pos.hwndInsertAfter = Native.HWND_BOTTOM;
            Marshal.StructureToPtr(pos, l, false);
        }
        return null;
    }

    async void LoadWallpaper()
    {
        var sb = new StringBuilder(1024);
        Native.SystemParametersInfo(0x73 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0);
        var path = sb.ToString();
        if (!File.Exists(path)) return;
        try
        {
            var bitmap = new BitmapImage { DecodePixelWidth = _bounds.Width };
            using (var stream = File.OpenRead(path))
                await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            Wallpaper.Source = bitmap;
        }
        catch (Exception e) { AppPaths.Log(e); }
    }

    // ---- Simgeler ----

    static IEnumerable<string> DesktopFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    }

    async void LoadItems()
    {
        var items = await Task.Run(() =>
        {
            var list = new List<DesktopItem>
            {
                new() { Name = "Bu bilgisayar", Path = ThisPc, IsSpecial = true },
                new() { Name = "Geri Dönüşüm Kutusu", Path = RecycleBin, IsSpecial = true },
            };
            var files = new List<DesktopItem>();
            foreach (var dir in DesktopFolders().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
                foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
                {
                    bool folder = info is DirectoryInfo;
                    var name = folder || !Shell.HidesExtension(info.Extension) ? info.Name : Path.GetFileNameWithoutExtension(info.Name);
                    files.Add(new DesktopItem { Name = name, Path = info.FullName, IsFolder = folder });
                }
            }
            list.AddRange(files.OrderByDescending(f => f.IsFolder).ThenBy(f => f.Name, Comparer<string>.Create(Shell.Compare)));
            return list;
        });

        var selected = Icons.SelectedItems.OfType<DesktopItem>().Select(i => i.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _items.Clear();
        int px = (int)Math.Round(40 * (Root.XamlRoot?.RasterizationScale ?? 1));
        foreach (var item in items)
        {
            _items.Add(item);
            IconCache.Instance!.Forget(item.Path, px); // simgesi degismis olabilir
            IconCache.Instance.Get(item.Path, px, src => item.Icon = src);
            if (selected.Contains(item.Path)) Icons.SelectedItems.Add(item);
        }
    }

    void Watch()
    {
        foreach (var dir in DesktopFolders().Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                void Poke() => DispatcherQueue.TryEnqueue(() => { _reload.Stop(); _reload.Start(); });
                watcher.Created += (_, _) => Poke();
                watcher.Deleted += (_, _) => Poke();
                watcher.Renamed += (_, _) => Poke();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch { }
        }
    }

    void Icons_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args) { }

    List<DesktopItem> Selected() => Icons.SelectedItems.OfType<DesktopItem>().ToList();

    static void Open(DesktopItem item, bool admin = false)
    {
        if (item.IsSpecial) AppCatalog.Open("explorer.exe", item.Path);
        else if (admin) AppCatalog.Launch(new AppEntry { Name = item.Name, Id = "file:" + item.Path, ExePath = item.Path }, admin: true);
        else AppCatalog.Open(item.Path);
    }

    void Icons_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is DesktopItem item) Open(item);
    }

    void Icons_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                foreach (var item in Selected()) Open(item);
                break;
            case VirtualKey.Delete:
                Delete(Selected());
                break;
            case VirtualKey.F5:
                LoadItems();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    async void Delete(List<DesktopItem> items)
    {
        var paths = items.Where(i => !i.IsSpecial).Select(i => i.Path).ToList();
        if (paths.Count > 0) await Shell.DeleteAsync(paths, permanent: false, Hwnd);
    }

    void Icons_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not DesktopItem item)
        {
            Icons.SelectedItems.Clear();
            return; // arka plan menusu Root'ta acilir
        }
        e.Handled = true;
        if (!Icons.SelectedItems.Contains(item))
        {
            Icons.SelectedItems.Clear();
            Icons.SelectedItems.Add(item);
        }

        var menu = new MenuFlyout();
        menu.Items.Add(TaskbarWindow.Entry("Aç", "", () => { foreach (var i in Selected()) Open(i); }));
        if (!item.IsSpecial)
        {
            var ext = Path.GetExtension(item.Path).ToLowerInvariant();
            if (ext is ".exe" or ".lnk" or ".bat" or ".cmd" or ".msi")
                menu.Items.Add(TaskbarWindow.Entry("Yönetici olarak çalıştır", "", () => Open(item, admin: true)));
            menu.Items.Add(TaskbarWindow.Entry("Yolu kopyala", "", () =>
            {
                var data = new DataPackage();
                data.SetText(string.Join(Environment.NewLine, Selected().Select(i => $"\"{i.Path}\"")));
                Clipboard.SetContent(data);
            }));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(TaskbarWindow.Entry("Sil", "", () => Delete(Selected())));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(TaskbarWindow.Entry("Özellikler", "", () => Shell.ShowProperties(Hwnd, item.Path)));
        menu.ShowAt(Icons, e.GetPosition(Icons));
    }

    void ShowBackgroundMenu(Windows.Foundation.Point at)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var menu = new MenuFlyout();
        if (_primary)
        {
            menu.Items.Add(TaskbarWindow.Entry("Yenile", "", LoadItems));
            var create = new MenuFlyoutSubItem { Text = "Yeni", Icon = new FontIcon { Glyph = "" } };
            create.Items.Add(TaskbarWindow.Entry("Klasör", "", () => CreateNew(desktop, "Yeni klasör", "", folder: true)));
            create.Items.Add(TaskbarWindow.Entry("Metin Belgesi", "", () => CreateNew(desktop, "Yeni Metin Belgesi", ".txt", folder: false)));
            menu.Items.Add(create);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        menu.Items.Add(TaskbarWindow.Entry("Terminalde aç", "", () => AppCatalog.Open("wt.exe", $"-d \"{desktop}\"")));
        menu.Items.Add(TaskbarWindow.Entry("Ekran ayarları", "", () => AppCatalog.Open("ms-settings:display")));
        menu.Items.Add(TaskbarWindow.Entry("Kişiselleştir", "", () => AppCatalog.Open("ms-settings:personalization-background")));
        menu.Items.Add(TaskbarWindow.Entry("Duvar kağıdını yenile", "", LoadWallpaper));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(TaskbarWindow.Entry("Kabuktan çık (Windows'a dön)", "", App.Current.Quit));
        menu.ShowAt(Root, at);
    }

    static void CreateNew(string dir, string baseName, string extension, bool folder)
    {
        var path = Path.Combine(dir, baseName + extension);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++)
            path = Path.Combine(dir, $"{baseName} ({n}){extension}");
        try
        {
            if (folder) Directory.CreateDirectory(path);
            else File.WriteAllText(path, "");
        }
        catch (Exception e) { AppPaths.Log(e); }
    }
}
