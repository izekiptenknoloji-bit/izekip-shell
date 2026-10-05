using Microsoft.Win32;

namespace IzekipShell.Services;

// Windows 11'in "Gorev cubugu hizalamasi" ayari: Ayarlar > Kisisellestirme > Gorev
// cubugu > Gorev cubugu davranislari > "Gorev cubugu ogelerini hizala" (Sol/Orta).
// Kendi ayri bir anahtarimiz yok; dogrudan Windows'un kayit defterinden okuyoruz ki
// kullanici o ayari degistirince bizim gorev cubugumuz da aynen uysun.
public static class TaskbarAlignment
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    // Deger yoksa Windows 11'in kendi varsayilani: ortali.
    public static bool Center
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue("TaskbarAl") is not int v || v != 0;
            }
            catch { return true; }
        }
    }
}
