using System.Collections.ObjectModel;
using System.Diagnostics;
using IzekipShell.Interop;
using IzekipShell.Models;
using IzekipShell.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace IzekipShell.Views;

// Ekranin altina yapisan gorev cubugu. AppBar olarak kaydolur: Windows calisma alanini
// buna gore kucultur, ekrani kaplayan pencereler ustune binmez.
public sealed partial class TaskbarWindow : Window
{
    const int HeightDip = 48;
    const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MINIMIZESTART = 0x16;

    readonly ObservableCollection<TaskItem> _items = new();
    readonly Dictionary<IntPtr, long> _firstSeen = new();
    readonly TranslateTransform _taskItemsOffset = new();
    double _taskItemsTarget;
    readonly IntPtr _hwnd;
    readonly OverlappedPresenter _presenter;
    readonly DispatcherQueueTimer _poll, _clock;
    uint _callbackMessage;
    bool _registered;
    long _seq;
    Native.WinEventProc? _winEventProc;
    IntPtr _winEventHook;
    int _statusTick;

    public RectInt32 BarBounds { get; private set; }
    public RectInt32 Monitor { get; private set; }
    public double Scale { get; private set; } = 1;

    public TaskbarWindow(RectInt32 monitor)
    {
        Monitor = monitor;
        InitializeComponent();
        SystemTheme.Bind(Root);
        Title = "İzekip Görev Çubuğu";
        _hwnd = WindowHelper.Handle(this);
        _presenter = WindowHelper.MakeChrome(this, topmost: true);
        int square = 1; // DWMWCP_DONOTROUND
        Native.DwmSetWindowAttribute(_hwnd, 33, ref square, 4);
        SystemBackdrop = new AlwaysAcrylic(DesktopAcrylicKind.Base);
        TaskItems.ItemsSource = _items;
        // Icerige gore boyutlanmasi icin sabit sola yasli: "ortalama" gorunumu RenderTransform ile kaydirilarak verilir.
        TaskItems.HorizontalAlignment = HorizontalAlignment.Left;
        TaskItems.RenderTransform = _taskItemsOffset;

        _poll = DispatcherQueue.CreateTimer();
        _poll.Interval = TimeSpan.FromMilliseconds(500);
        _poll.Tick += (_, _) => Refresh();

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) =>
        {
            UpdateTray();
            // DWM bazen odak/tema degisince cerceveyi geri ekliyor; ucuz oldugu icin her saniye yeniden silinir.
            WindowHelper.StripFrame(_hwnd);
            WindowHelper.NoBorder(_hwnd);
        };

        AppCatalog.Changed += Refresh;
        ShellSettings.Changed += Refresh;
        WindowsUpdate.Changed += () => DispatcherQueue.TryEnqueue(() => UpdateTray(force: true));
    }

    public void ShowBar()
    {
        // DPI pencerenin bulundugu ekrandan okunur: once ana ekrana tasi.
        AppWindow.MoveAndResize(new RectInt32(Monitor.X, Monitor.Y + Monitor.Height - 100, Monitor.Width, 100));
        Scale = WindowHelper.Scale(_hwnd);

        _callbackMessage = Native.RegisterWindowMessage("IzekipShell.AppBar");
        WindowHelper.Subclass(_hwnd, OnMessage);
        var abd = Native.AppBar(_hwnd);
        abd.uCallbackMessage = _callbackMessage;
        Native.SHAppBarMessage(Native.ABM_NEW, ref abd);
        _registered = true;
        Position();

        Activate();
        TrackForeground();
        UpdateTray();
        Refresh();
        _poll.Start();
        _clock.Start();
        if (WindowsUpdate.State == UpdateState.Unknown) WindowsUpdate.RefreshAsync();
    }

    void Position()
    {
        int h = (int)Math.Round(HeightDip * Scale);
        var abd = Native.AppBar(_hwnd);
        abd.uEdge = Native.ABE_BOTTOM;
        abd.rc = new Native.RECT
        {
            left = Monitor.X,
            right = Monitor.X + Monitor.Width,
            bottom = Monitor.Y + Monitor.Height,
            top = Monitor.Y + Monitor.Height - h,
        };
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        abd.rc.top = abd.rc.bottom - h;
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);
        BarBounds = new RectInt32(abd.rc.left, abd.rc.top, abd.rc.right - abd.rc.left, h);
        AppWindow.MoveAndResize(BarBounds);
    }

    public void Unregister()
    {
        if (!_registered) return;
        _registered = false;
        _poll.Stop();
        _clock.Stop();
        if (_winEventHook != 0) Native.UnhookWinEvent(_winEventHook);
        var abd = Native.AppBar(_hwnd);
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
    }

    IntPtr? OnMessage(uint msg, IntPtr w, IntPtr l)
    {
        if (msg != _callbackMessage || _callbackMessage == 0) return null;
        switch ((int)w)
        {
            // Tam ekran oyun/video acilinca gorev cubugu ustte kalmasin.
            case Native.ABN_FULLSCREENAPP:
                _presenter.IsAlwaysOnTop = l == 0;
                if (l != 0) Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                break;
            case Native.ABN_POSCHANGED:
                if (_registered) Position();
                break;
        }
        return 0;
    }

    // ---- Popup yerlesimi (fiziksel piksel) ----

    public RectInt32 PopupRect(double widthDip, double heightDip, bool right, double gapDip = 12)
    {
        int margin = (int)Math.Round(gapDip * Scale);
        int w = (int)Math.Round(widthDip * Scale);
        int h = Math.Min((int)Math.Round(heightDip * Scale), BarBounds.Y - Monitor.Y - 2 * margin);
        int x = right ? Monitor.X + Monitor.Width - margin - w : Monitor.X + margin;
        if (margin == 0) h = Math.Min((int)Math.Round(heightDip * Scale), BarBounds.Y - Monitor.Y);
        return new RectInt32(x, BarBounds.Y - margin - h, w, h);
    }

    // Arama kutusunun sol kenari (fiziksel piksel): arama penceresi buraya hizalanir.
    public int SearchAnchorX
    {
        get
        {
            var at = SearchPill.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0, 0));
            return BarBounds.X + (int)Math.Round(at.X * Scale);
        }
    }

    // ---- Pencere listesi ----

    void TrackForeground()
    {
        _winEventProc = (_, ev, hwnd, idObject, _, _, _) =>
        {
            if (idObject != 0 || hwnd == 0) return;
            if (ev == EVENT_SYSTEM_FOREGROUND)
            {
                App.Current.StartMenu?.ForegroundChanged(hwnd);
                App.Current.Panel?.ForegroundChanged(hwnd);
                App.Current.Search?.ForegroundChanged(hwnd);
                App.Current.Clock?.ForegroundChanged(hwnd);
                if (Native.ProcessId(hwnd) != (uint)Environment.ProcessId) WindowTracker.LastActive = hwnd;
                else if (App.Current.IsDesktopWindow(hwnd)) WindowTracker.LastActive = 0;
            }
            else if (ev == EVENT_SYSTEM_MINIMIZESTART && hwnd == WindowTracker.LastActive)
                WindowTracker.LastActive = 0;
            Refresh();
        };
        _winEventHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MINIMIZESTART, 0, _winEventProc, 0, 0, 0);
        var fg = Native.GetForegroundWindow();
        if (Native.ProcessId(fg) != (uint)Environment.ProcessId) WindowTracker.LastActive = fg;
    }

    // Windows'un "Gorev cubugu hizalamasi" ayarina uyar; sol<->orta gecisi yumusakca kayarak olur.
    void ApplyAlignment()
    {
        double target = 0;
        if (TaskbarAlignment.Center)
        {
            double colW = Root.ColumnDefinitions[1].ActualWidth;
            double itemsW = TaskItems.ActualWidth;
            if (colW > 0 && itemsW > 0) target = Math.Max(0, (colW - itemsW) / 2);
        }
        if (Math.Abs(target - _taskItemsTarget) < 0.5) return;
        _taskItemsTarget = target;

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(380),
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 },
        };
        Storyboard.SetTarget(anim, _taskItemsOffset);
        Storyboard.SetTargetProperty(anim, "X");
        var board = new Storyboard();
        board.Children.Add(anim);
        board.Begin();
    }

    void Refresh()
    {
        System.IO.File.AppendAllText(AppPaths.File("debug.log"), $"{DateTime.Now:HH:mm:ss} Center={TaskbarAlignment.Center}" + Environment.NewLine);
        ApplyAlignment();

        var windows = WindowTracker.Enumerate();
        foreach (var w in windows) if (!_firstSeen.ContainsKey(w.Hwnd)) _firstSeen[w.Hwnd] = _seq++;
        foreach (var gone in _firstSeen.Keys.Where(h => windows.All(w => w.Hwnd != h)).ToList()) _firstSeen.Remove(gone);

        var existing = _items.ToDictionary(i => i.Key);
        var desired = new List<TaskItem>();

        foreach (var id in ShellSettings.Current.TaskbarPins)
        {
            if (AppCatalog.FindById(id) is not { } app) continue;
            var item = existing.GetValueOrDefault("pin:" + id) ?? new TaskItem("pin:" + id);
            item.App = app;
            item.IsPinned = true;
            item.Windows.Clear();
            desired.Add(item);
        }

        foreach (var w in windows.OrderBy(w => _firstSeen[w.Hwnd]))
        {
            var target = desired.FirstOrDefault(i => i.IsPinned && i.Matches(w));
            if (target is null)
            {
                var key = w.Aumid ?? w.Exe ?? w.Hwnd.ToString();
                target = desired.FirstOrDefault(i => i.Key == key);
                if (target is null)
                {
                    target = existing.GetValueOrDefault(key) ?? new TaskItem(key);
                    target.IsPinned = false;
                    target.App ??= AppCatalog.Find(w.Aumid, w.Exe);
                    target.Windows.Clear();
                    desired.Add(target);
                }
            }
            target.Windows.Add(w);
        }

        var active = WindowTracker.LastActive;
        foreach (var item in desired)
        {
            item.IsRunning = item.Windows.Count > 0;
            item.IsActive = item.Windows.Any(w => w.Hwnd == active);
            item.Tooltip = item.Windows.Count switch
            {
                0 => item.Name,
                1 => item.Windows[0].Title,
                _ => item.Name + "\n" + string.Join("\n", item.Windows.Select(w => "• " + w.Title)),
            };
            RequestIcon(item);
        }

        for (int i = 0; i < desired.Count; i++)
        {
            int at = _items.IndexOf(desired[i]);
            if (at < 0) _items.Insert(i, desired[i]);
            else if (at != i) _items.Move(at, i);
        }
        while (_items.Count > desired.Count) _items.RemoveAt(_items.Count - 1);
    }

    void RequestIcon(TaskItem item)
    {
        var first = item.Windows.FirstOrDefault();
        string? primary = item.App?.IconPath ?? (first?.Aumid is { } aumid ? @"shell:AppsFolder\" + aumid : first?.Exe);
        var fallback = first?.Exe ?? item.App?.ExePath;
        if (primary is null || item.IconFor == primary) return;
        item.IconFor = primary;
        IconCache.Instance!.Get(primary, fallback, (int)Math.Round(24 * Scale), src => item.Icon = src);
    }

    // ---- Olaylar ----

    void TaskItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TaskItem item) return;
        var windows = item.Windows;
        if (windows.Count == 0)
        {
            if (item.App is not null) AppCatalog.Launch(item.App);
            return;
        }
        if (windows.Count == 1)
        {
            var h = windows[0].Hwnd;
            if (h == WindowTracker.LastActive && !Native.IsIconic(h)) WindowTracker.Minimize(h);
            else WindowTracker.Activate(h);
            Refresh();
            return;
        }

        var menu = Menu();
        foreach (var w in windows)
            menu.Items.Add(Entry(w.Title, w.Hwnd == WindowTracker.LastActive ? "" : "", () => { WindowTracker.Activate(w.Hwnd); Refresh(); }));
        menu.ShowAt((FrameworkElement)sender);
    }

    void TaskItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not TaskItem item) return;
        var menu = Menu();
        foreach (var w in item.Windows.Take(8))
            menu.Items.Add(Entry(w.Title, "", () => WindowTracker.Activate(w.Hwnd)));
        if (item.Windows.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());

        if (item.App is { } app)
        {
            menu.Items.Add(Entry(app.Name, "", () => AppCatalog.Launch(app)));
            bool pinned = ShellSettings.Current.TaskbarPins.Contains(app.Id);
            menu.Items.Add(Entry(pinned ? "Görev çubuğundan kaldır" : "Görev çubuğuna sabitle", pinned ? "" : "",
                () => ShellSettings.Toggle(ShellSettings.Current.TaskbarPins, app.Id)));
        }
        else if (item.Windows.FirstOrDefault()?.Exe is { } exe)
        {
            menu.Items.Add(Entry(Path.GetFileNameWithoutExtension(exe), "", () => AppCatalog.Open(exe)));
        }

        if (item.Windows.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Entry(item.Windows.Count > 1 ? "Tüm pencereleri kapat" : "Pencereyi kapat", "", () =>
            {
                foreach (var w in item.Windows) WindowTracker.Close(w.Hwnd);
            }));
        }
        menu.ShowAt((FrameworkElement)sender);
    }

    void Root_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var menu = Menu();
        menu.Items.Add(Entry("Görev Yöneticisi", "", () => AppCatalog.Open("taskmgr.exe")));
        menu.Items.Add(Entry("Masaüstünü göster", "", WindowTracker.ToggleDesktop));
        menu.Items.Add(Entry("Ayarlar", "", () => AppCatalog.Open("ms-settings:")));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Entry("Kabuktan çık (Windows'a dön)", "", App.Current.Quit));
        menu.ShowAt(Root, e.GetPosition(Root));
    }

    void Start_Click(object sender, RoutedEventArgs e) { App.Current.Taskbar = this; App.Current.StartMenu.Toggle(); }
    void Search_Click(object sender, RoutedEventArgs e) { App.Current.Taskbar = this; App.Current.Search.Toggle(); }
    void Panel_Click(object sender, RoutedEventArgs e) { App.Current.Taskbar = this; App.Current.Panel.Toggle(); }
    void Clock_Click(object sender, RoutedEventArgs e) { App.Current.Taskbar = this; App.Current.Clock.Toggle(); }

    void ShowDesktop_Tapped(object sender, TappedRoutedEventArgs e) => WindowTracker.ToggleDesktop();
    void ShowDesktop_PointerEntered(object sender, PointerRoutedEventArgs e) => ShowDesktopLine.Height = 32;
    void ShowDesktop_PointerExited(object sender, PointerRoutedEventArgs e) => ShowDesktopLine.Height = 20;

    void Tray_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (Audio.Get() is not { } v) return;
        int delta = e.GetCurrentPoint(TrayButton).Properties.MouseWheelDelta > 0 ? 2 : -2;
        Audio.SetLevel(v.Level + delta);
        UpdateTray();
        e.Handled = true;
    }

    // ---- Bildirim alani ----

    public void UpdateTray(bool force = false)
    {
        var now = DateTime.Now;
        TimeText.Text = FocusTimer.Remaining is { } left
            ? "⏱ " + left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss")
            : now.ToString("HH:mm", Format.Tr);
        DateText.Text = now.ToString("dd.MM.yyyy", Format.Tr);

        var volume = Audio.Get();
        VolumeGlyph.Glyph = volume is { } v ? SystemStatus.VolumeGlyph(v.Level, v.Muted) : "";
        ToolTipService.SetToolTip(VolumeGlyph, volume is { } t ? (t.Muted ? "Ses kapalı" : $"Ses: %{t.Level}") : "Ses aygıtı yok");

        // Ag ve pil yavas degisir; her 5 saniyede bir yeter.
        if (_statusTick++ % 5 != 0 && !force) return;
        var net = SystemStatus.Network();
        NetGlyph.Glyph = net.Glyph;
        NetGlyph.Opacity = net.Online ? 1 : 0.5;
        ToolTipService.SetToolTip(TrayButton, net.Text);

        var battery = SystemStatus.Battery();
        BatteryGlyph.Visibility = battery.Present ? Visibility.Visible : Visibility.Collapsed;
        if (battery.Present)
        {
            BatteryGlyph.Glyph = battery.Glyph;
            ToolTipService.SetToolTip(BatteryGlyph, battery.Text);
        }

        bool needsAttention = WindowsUpdate.State is UpdateState.Available or UpdateState.RebootRequired;
        UpdateIndicator.Visibility = needsAttention ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadge.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[WindowsUpdate.State == UpdateState.RebootRequired
            ? "SystemFillColorCriticalBrush" : "SystemFillColorCautionBrush"];
        ToolTipService.SetToolTip(UpdateIndicator, WindowsUpdate.Summary);
    }

    // Gorev cubugu 48 DIP; menuler pencere disina tasabilmeli.
    static MenuFlyout Menu() => new() { ShouldConstrainToRootBounds = false, Placement = FlyoutPlacementMode.Top };

    internal static MenuFlyoutItem Entry(string text, string glyph, Action action)
    {
        var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        entry.Click += (_, _) => action();
        return entry;
    }
}
