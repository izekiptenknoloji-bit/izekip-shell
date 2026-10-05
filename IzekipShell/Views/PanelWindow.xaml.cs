using IzekipShell.Services;
using Microsoft.UI.Dispatching;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace IzekipShell.Views;

public sealed partial class PanelWindow : Window
{
    readonly PopupController _popup;
    readonly DispatcherQueueTimer _timer;
    bool _updating;

    public PanelWindow()
    {
        InitializeComponent();
        SystemTheme.Bind(Root);
        Title = "İzekip Denetim Merkezi";
        _popup = new PopupController(this, Root);
        _popup.Opened += () =>
        {
            ShowMain();
            Update();
            _timer.Start();
        };
        WindowsUpdate.Changed += () => DispatcherQueue.TryEnqueue(Update);
        AppUpdater.Changed += () => DispatcherQueue.TryEnqueue(Update);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            if (_popup.IsOpen) Update();
            else _timer.Stop();
        };
    }

    public void Toggle() => _popup.Toggle(() => App.Current.Taskbar.PopupRect(380, 530, right: true));

    public void ForegroundChanged(IntPtr h) => _popup.ForegroundChanged(h);

    void Update()
    {
        var now = DateTime.Now;

        _updating = true;
        if (Audio.Get() is { } v)
        {
            VolumeSlider.IsEnabled = MuteButton.IsEnabled = true;
            VolumeSlider.Value = v.Level;
            VolumeText.Text = v.Level.ToString();
            VolumeIcon.Glyph = SystemStatus.VolumeGlyph(v.Level, v.Muted);
        }
        else
        {
            VolumeSlider.IsEnabled = MuteButton.IsEnabled = false;
            VolumeText.Text = "";
            VolumeIcon.Glyph = "";
        }
        _updating = false;

        var net = SystemStatus.Network();
        NetIcon.Glyph = net.Glyph;
        NetText.Text = net.Text;
        WifiTileText.Text = net.Glyph == "\uE701" ? net.Text.Split(" · ")[0] : "Wi-Fi";

        var battery = SystemStatus.Battery();
        BatteryRow.Visibility = battery.Present ? Visibility.Visible : Visibility.Collapsed;
        if (battery.Present)
        {
            BatteryIcon.Glyph = battery.Glyph;
            BatteryText.Text = battery.Text;
        }

        UpdateText.Text = WindowsUpdate.Summary;
        UpdateIcon.Glyph = WindowsUpdate.State switch
        {
            UpdateState.RebootRequired => "",
            UpdateState.Available => "",
            UpdateState.Checking => "",
            _ => "",
        };
        UpdateIcon.Foreground = WindowsUpdate.State is UpdateState.Available or UpdateState.RebootRequired
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        UpdateRefresh.IsEnabled = WindowsUpdate.State != UpdateState.Checking;

        AppUpdateText.Text = "İzekip Shell — " + AppUpdater.Summary;
        AppUpdateIcon.Glyph = AppUpdater.State switch
        {
            AppUpdateState.ReadyToInstall => "",
            AppUpdateState.UpToDate => "",
            AppUpdateState.Error => "",
            _ => "",
        };
        AppUpdateIcon.Foreground = AppUpdater.State is AppUpdateState.Available or AppUpdateState.Downloading or AppUpdateState.ReadyToInstall
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
    }

    // Kart tek tiklama alani: kabugun kendi guncellemesinde yapilacak bir sey varsa
    // (ayrintili kart Baslat menusunde) oraya, yoksa Windows Update sayfasina gider.
    void UpdateRow_Click(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        bool appActionable = AppUpdater.State is AppUpdateState.Available or AppUpdateState.Downloading or AppUpdateState.ReadyToInstall;
        if (appActionable) App.Current.StartMenu.Toggle();
        else AppCatalog.Open("ms-settings:windowsupdate");
    }

    void UpdateRefresh_Click(object sender, RoutedEventArgs e)
    {
        WindowsUpdate.RefreshAsync();
        Update();
    }

    // ---- Wi-Fi ----

    WifiNetwork? _selected;
    bool _connecting;

    void ShowMain()
    {
        WifiView.Visibility = Visibility.Collapsed;
        MainView.Visibility = Visibility.Visible;
    }

    void WifiTile_Click(object sender, RoutedEventArgs e)
    {
        MainView.Visibility = Visibility.Collapsed;
        WifiView.Visibility = Visibility.Visible;
        ConnectCard.Visibility = Visibility.Collapsed;
        WindowHelper.Entrance(WifiView, 24);
        LoadNetworks(scan: true);
    }

    void WifiBack_Click(object sender, RoutedEventArgs e) => ShowMain();
    void WifiRefresh_Click(object sender, RoutedEventArgs e) => LoadNetworks(scan: true);

    // Once eldeki liste hemen gosterilir, tarama bitince tazelenir.
    async void LoadNetworks(bool scan)
    {
        WifiProgress.Visibility = Visibility.Visible;
        WifiList.ItemsSource = await Task.Run(Wifi.Networks);
        if (scan)
        {
            await Task.Run(Wifi.Scan);
            await Task.Delay(3500);
            if (WifiView.Visibility == Visibility.Visible)
            {
                var selectedSsid = _selected?.Ssid;
                var list = await Task.Run(Wifi.Networks);
                WifiList.ItemsSource = list;
                if (selectedSsid is not null) WifiList.SelectedItem = list.FirstOrDefault(n => n.Ssid == selectedSsid);
            }
        }
        if (!_connecting) WifiProgress.Visibility = Visibility.Collapsed;
    }

    void Wifi_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WifiNetwork n) return;
        _selected = n;
        ConnectTitle.Text = n.Ssid;
        ConnectError.Visibility = Visibility.Collapsed;
        WifiPassword.Password = "";
        WifiPassword.Visibility = n.NeedsPassword && !n.Connected ? Visibility.Visible : Visibility.Collapsed;
        ConnectButton.Content = n.Connected ? "Bağlantıyı kes" : "Bağlan";
        ConnectButton.IsEnabled = true;
        ConnectCard.Visibility = Visibility.Visible;
        if (WifiPassword.Visibility == Visibility.Visible) WifiPassword.Focus(FocusState.Programmatic);
    }

    void WifiPassword_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        Connect_Click(sender, e);
    }

    async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } n || _connecting) return;
        if (n.Connected)
        {
            await Task.Run(Wifi.Disconnect);
            ConnectCard.Visibility = Visibility.Collapsed;
            await Task.Delay(800);
            LoadNetworks(scan: false);
            return;
        }

        _connecting = true;
        ConnectButton.IsEnabled = false;
        ConnectButton.Content = "Bağlanıyor…";
        ConnectError.Visibility = Visibility.Collapsed;
        WifiProgress.Visibility = Visibility.Visible;
        var error = await Wifi.ConnectAsync(n, WifiPassword.Password);
        _connecting = false;
        WifiProgress.Visibility = Visibility.Collapsed;
        ConnectButton.IsEnabled = true;
        ConnectButton.Content = "Bağlan";
        if (error is not null)
        {
            ConnectError.Text = error;
            ConnectError.Visibility = Visibility.Visible;
            return;
        }
        ConnectCard.Visibility = Visibility.Collapsed;
        LoadNetworks(scan: false);
        App.Current.Taskbar.UpdateTray(force: true);
    }

    void WifiSettings_Click(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        AppCatalog.Open("ms-settings:network-wifi");
    }

    void Volume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        Audio.SetLevel((int)e.NewValue);
        VolumeText.Text = ((int)e.NewValue).ToString();
        VolumeIcon.Glyph = SystemStatus.VolumeGlyph((int)e.NewValue, false);
        App.Current.Taskbar.UpdateTray();
    }

    void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (Audio.Get() is not { } v) return;
        Audio.SetMuted(!v.Muted);
        Update();
        App.Current.Taskbar.UpdateTray();
    }

    void Quick_Click(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        AppCatalog.Open((string)((FrameworkElement)sender).Tag);
    }

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        _popup.Hide();
        e.Handled = true;
    }
}
