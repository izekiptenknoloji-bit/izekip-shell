using IzekipShell.Interop;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;

namespace IzekipShell.Services;

public static class WindowHelper
{
    static readonly List<Native.SubclassProc> KeepAlive = new();
    static uint _nextId = 1;

    public static IntPtr Handle(Window w) => WindowNative.GetWindowHandle(w);

    public static double Scale(IntPtr h) => Math.Max(1, Native.GetDpiForWindow(h)) / 96.0;

    // Kenarliksiz, Alt+Tab'da gorunmeyen kabuk penceresi.
    public static OverlappedPresenter MakeChrome(Window w, bool topmost)
    {
        var p = OverlappedPresenter.Create();
        p.IsResizable = false;
        p.IsMaximizable = false;
        p.IsMinimizable = false;
        p.IsAlwaysOnTop = topmost;
        p.SetBorderAndTitleBar(false, false);
        w.AppWindow.SetPresenter(p);
        w.AppWindow.IsShownInSwitchers = false;

        var h = Handle(w);
        StripFrame(h);
        // DWM'in standart pencere golgesini/ince cercevesini tamamen kapatir (ustteki parlak cizginin kaynagi).
        Subclass(h, (msg, wp, _) => msg == 0x83 /* WM_NCCALCSIZE */ && wp != 0 ? 0 : null);
        long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, (IntPtr)(ex | Native.WS_EX_TOOLWINDOW));
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        Native.DwmSetWindowAttribute(h, 34 /* DWMWA_BORDER_COLOR */, ref none, 4);
        Native.SetWindowPos(h, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | 0x20 /* SWP_FRAMECHANGED */);
        return p;
    }

    // WinUI kenarliksiz pencereye ince diyalog cercevesi birakiyor; onu siler (yuvarlak koseler kalir).
    public static void StripFrame(IntPtr h)
    {
        long style = Native.GetWindowLongPtr(h, -16 /* GWL_STYLE */);
        long clean = style & ~(0x00800000L | 0x00400000L | 0x00040000L); // WS_BORDER | WS_DLGFRAME | WS_THICKFRAME
        long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        long cleanEx = ex & ~0x100L; // WS_EX_WINDOWEDGE
        if (clean == style && cleanEx == ex) return;
        Native.SetWindowLongPtr(h, -16, (IntPtr)clean);
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, (IntPtr)cleanEx);
        Native.SetWindowPos(h, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | 0x20 /* SWP_FRAMECHANGED */);
    }

    // DWM'in pencere cevresine cizdigi ince acik cizgi kapatilir.
    public static void NoBorder(IntPtr h)
    {
        int none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        Native.DwmSetWindowAttribute(h, 34 /* DWMWA_BORDER_COLOR */, ref none, 4);
    }

    public static void RoundCorners(IntPtr h)
    {
        int round = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(h, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref round, 4);
    }

    // handler null donerse ileti olagan yoluna devam eder.
    public static void Subclass(IntPtr h, Func<uint, IntPtr, IntPtr, IntPtr?> handler)
    {
        Native.SubclassProc proc = (hwnd, msg, wp, lp, _, _) => handler(msg, wp, lp) ?? Native.DefSubclassProc(hwnd, msg, wp, lp);
        KeepAlive.Add(proc);
        Native.SetWindowSubclass(h, proc, _nextId++, 0);
    }

    public static RectInt32 Rect(int x, int y, int w, int h) => new(x, y, w, h);

    // Fluent giris: icerik asagidan kayarak ve belirerek gelir.
    public static void Entrance(UIElement element, double from = 40)
    {
        var move = new TranslateTransform();
        element.RenderTransform = move;
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        var slide = new DoubleAnimation { From = from, To = 0, Duration = TimeSpan.FromMilliseconds(380), EasingFunction = ease };
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(slide, move);
        Storyboard.SetTargetProperty(slide, "Y");
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(slide);
        board.Children.Add(fade);
        board.Begin();
    }

    // Cikis: kisa, hizlanarak asagi kayip kaybolur; bitince done cagrilir.
    public static void Exit(UIElement element, Action done, double to = 24)
    {
        DependencyObject move;
        string axis;
        if (element.RenderTransform is CompositeTransform composite) { move = composite; axis = "TranslateY"; }
        else
        {
            var translate = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
            element.RenderTransform = translate;
            move = translate;
            axis = "Y";
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slide = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = ease };
        var fade = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = ease };
        Storyboard.SetTarget(slide, move);
        Storyboard.SetTargetProperty(slide, axis);
        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(slide);
        board.Children.Add(fade);
        board.Completed += (_, _) => done();
        board.Begin();
    }
}

// Pencere etkin olmasa da akrilik kalsin: gorev cubugu neredeyse hic etkin olmaz,
// varsayilan DesktopAcrylicBackdrop ise etkin degilken duz renge doner.
public sealed class AlwaysAcrylic : SystemBackdrop
{
    readonly DesktopAcrylicKind _kind;
    DesktopAcrylicController? _controller;
    SystemBackdropConfiguration? _config;
    FrameworkElement? _root;

    public AlwaysAcrylic(DesktopAcrylicKind kind = DesktopAcrylicKind.Default) => _kind = kind;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        _config = new SystemBackdropConfiguration { IsInputActive = true };
        _root = xamlRoot.Content as FrameworkElement;
        if (_root is not null) _root.ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        _controller = new DesktopAcrylicController { Kind = _kind };
        _controller.AddSystemBackdropTarget(target);
        _controller.SetSystemBackdropConfiguration(_config);
    }


    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);
        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }

    void ApplyTheme()
    {
        if (_config is null) return;
        _config.Theme = _root?.ActualTheme switch
        {
            ElementTheme.Light => SystemBackdropTheme.Light,
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            _ => SystemBackdropTheme.Default,
        };
    }
}

// Baslat ve denetim merkezi: gorev cubugunun ustunde acilan, odak kaybedince kapanan pencere.
public sealed class PopupController
{
    readonly Window _window;
    readonly UIElement _content;
    readonly IntPtr _hwnd;
    DateTime _hiddenAt, _shownAt;

    public bool IsOpen { get; private set; }
    public event Action? Opened;

    public PopupController(Window window, UIElement content)
    {
        _window = window;
        _content = content;
        _hwnd = WindowHelper.Handle(window);
        WindowHelper.MakeChrome(window, topmost: true);
        WindowHelper.RoundCorners(_hwnd);
        var margins = new Native.MARGINS { cxLeftWidth = 1, cxRightWidth = 1, cyTopHeight = 1, cyBottomHeight = 1 };
        Native.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
        window.SystemBackdrop = new AlwaysAcrylic();
        window.Activated += (_, e) =>
        {
            // Acilirken gelen gecici odak kaybi sayilmaz.
            if (e.WindowActivationState == WindowActivationState.Deactivated && IsOpen && !JustShown) Hide();
        };
    }

    bool JustShown => (DateTime.UtcNow - _shownAt).TotalMilliseconds < 500;

    // On plandaki pencere degisince (gorev cubugu kancasindan) baska yere tiklandiysa kapanir.
    public void ForegroundChanged(IntPtr h)
    {
        if (IsOpen && h != _hwnd && !JustShown) Hide();
    }


    public void Show(RectInt32 rect)
    {
        // Baska DPI'li ekrana geciste Windows boyutu olceklendirir; ikinci cagri tam yerine oturtur.
        _window.AppWindow.MoveAndResize(rect);
        _window.AppWindow.MoveAndResize(rect);
        IsOpen = true;
        _shownAt = DateTime.UtcNow;
        _window.Activate();
        WindowHelper.StripFrame(_hwnd);
        WindowHelper.NoBorder(_hwnd);
        WindowHelper.RoundCorners(_hwnd);
        Native.SetForegroundWindow(_hwnd);
        Opened?.Invoke();
        WindowHelper.Entrance(_content);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _hiddenAt = DateTime.UtcNow;
        // Animasyon bitmeden yeniden acildiysa gizleme.
        WindowHelper.Exit(_content, () =>
        {
            // Kabuk kapanirken pencere coktan yok olmus olabilir.
            try { if (!IsOpen) _window.AppWindow?.Hide(); } catch { }
        });
    }

    // Acikken dugmeye basmak once odak kaybiyla kapatir; ayni tiklama yeniden acmasin.
    public void Toggle(Func<RectInt32> rect)
    {
        if (IsOpen) Hide();
        else if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds > 300) Show(rect());
    }
}
