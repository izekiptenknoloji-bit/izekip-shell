using IzekipShell.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace IzekipShell.Views;

public sealed partial class ClockWindow : Window
{
    readonly PopupController _popup;
    readonly DispatcherQueueTimer _timer;

    public ClockWindow()
    {
        InitializeComponent();
        SystemTheme.Bind(Root);
        Title = "İzekip Saat";
        _popup = new PopupController(this, Root);
        _popup.Opened += () =>
        {
            Calendar.SetDisplayDate(DateTimeOffset.Now);
            Update();
            _timer.Start();
        };

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) =>
        {
            if (_popup.IsOpen) Update();
            else _timer.Stop();
        };
    }

    public void Toggle() => _popup.Toggle(() => App.Current.Taskbar.PopupRect(390, 700, right: true));

    public void ForegroundChanged(IntPtr h) => _popup.ForegroundChanged(h);

    void Update()
    {
        var now = DateTime.Now;
        BigTime.Text = now.ToString("HH:mm", Format.Tr);
        Seconds.Text = now.ToString("ss", Format.Tr);
        LongDate.Text = now.ToString("d MMMM yyyy, dddd", Format.Tr);

        if (FocusTimer.Remaining is { } left)
        {
            FocusTitle.Text = "Odaklan · " + left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss") + " kaldı";
            FocusRing.Value = FocusTimer.Progress;
            FocusChoices.Visibility = Visibility.Collapsed;
            FocusStop.Visibility = Visibility.Visible;
        }
        else
        {
            FocusTitle.Text = "Odak zamanlayıcısı";
            FocusRing.Value = 0;
            FocusChoices.Visibility = Visibility.Visible;
            FocusStop.Visibility = Visibility.Collapsed;
        }
    }

    void Focus_Click(object sender, RoutedEventArgs e)
    {
        FocusTimer.Start(TimeSpan.FromMinutes(int.Parse((string)((FrameworkElement)sender).Tag)));
        Update();
        foreach (var bar in App.Current.Taskbars) bar.UpdateTray();
    }

    void FocusStop_Click(object sender, RoutedEventArgs e)
    {
        FocusTimer.Stop();
        Update();
        foreach (var bar in App.Current.Taskbars) bar.UpdateTray();
    }

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        _popup.Hide();
        e.Handled = true;
    }
}
