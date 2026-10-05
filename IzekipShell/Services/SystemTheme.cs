using System.Runtime.InteropServices;
using IzekipShell.Interop;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace IzekipShell.Services;

// Windows'ta "Varsayilan Windows modu" ile "Varsayilan uygulama modu" ayri ayardir
// (Ayarlar > Kisisellestirme > Renkler > Ozel). Gorev cubugu/Baslat gibi sistem
// yuzeyleri hep "Windows modu"nu (SystemUsesLightTheme) takip eder, tek tek
// uygulamalarin kendi secimini (AppsUseLightTheme) degil. Biz de kabuk olarak
// ayni kurali uyguluyoruz: kendi pencerelerimiz sistem moduna gore koyu/acik olur.
public static class SystemTheme
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static ElementTheme Current { get; private set; } = Read();
    public static event Action? Changed;

    static ElementTheme Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            // Deger yoksa Windows'un kendi varsayilani: acik.
            var light = key?.GetValue("SystemUsesLightTheme") is int v ? v != 0 : true;
            return light ? ElementTheme.Light : ElementTheme.Dark;
        }
        catch { return ElementTheme.Dark; }
    }

    static void Refresh()
    {
        var next = Read();
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
    }

    // Bir pencereyi sistem temasina baglar: hemen uygular, degisince canli gunceller.
    public static void Bind(FrameworkElement root)
    {
        root.RequestedTheme = Current;
        Changed += () => root.DispatcherQueue.TryEnqueue(() => root.RequestedTheme = Current);
    }

    // Tek bir pencereden cagrilir yeter: Windows tema degisince WM_SETTINGCHANGE/"ImmersiveColorSet" yayinlar.
    public static void Watch(IntPtr hwnd) =>
        WindowHelper.Subclass(hwnd, (msg, _, lp) =>
        {
            if (msg == 0x001A /* WM_SETTINGCHANGE */ && lp != 0)
            {
                string? name = null;
                try { name = Marshal.PtrToStringUni(lp); } catch { }
                if (name == "ImmersiveColorSet") Refresh();
            }
            return null;
        });
}
