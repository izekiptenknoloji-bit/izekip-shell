using IzekipShell.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace IzekipShell.Views;

// --guncelleme-tamamla modunda acilan tek pencere. Eski surum kapanana kadar, dosyalar
// kopyalanirken ve yeni surum acilana kadar ekranda tek basina kalir.
public sealed partial class UpdatingWindow : Window
{
    public UpdatingWindow()
    {
        InitializeComponent();
        Title = "Explorer33 Güncelleniyor";
        SystemTheme.Bind(Root);

        var hwnd = WindowHelper.Handle(this);
        WindowHelper.MakeChrome(this, topmost: true);
        WindowHelper.StripFrame(hwnd);
        WindowHelper.NoBorder(hwnd);
        WindowHelper.RoundCorners(hwnd);
        SystemBackdrop = new AlwaysAcrylic();

        double scale = WindowHelper.Scale(hwnd);
        var area = DisplayArea.Primary.WorkArea;
        int w = (int)Math.Round(360 * scale), h = (int)Math.Round(210 * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        Activate();
    }

    public void SetStatus(string text) => DispatcherQueue.TryEnqueue(() => StatusText.Text = text);
}
