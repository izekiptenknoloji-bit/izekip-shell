using Windows.Devices.Power;
using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace IzekipShell.Services;

// Bildirim alani gostergeleri (Segoe Fluent Icons glifleri).
public static class SystemStatus
{
    public static string VolumeGlyph(int level, bool muted) =>
        muted ? "" : level == 0 ? "" : level < 34 ? "" : level < 67 ? "" : "";

    public static (string Glyph, string Text, bool Online) Network()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return ("", "Bağlantı yok", false);
            var level = profile.GetNetworkConnectivityLevel();
            bool online = level == NetworkConnectivityLevel.InternetAccess;
            var glyph = profile.IsWlanConnectionProfile ? "" : "";
            var name = profile.ProfileName;
            return (glyph, online ? name : $"{name} · İnternet yok", online);
        }
        catch { return ("", "Ağ durumu okunamadı", false); }
    }

    public static (bool Present, string Glyph, string Text) Battery()
    {
        try
        {
            var report = Windows.Devices.Power.Battery.AggregateBattery.GetReport();
            if (report.Status == BatteryStatus.NotPresent || report.FullChargeCapacityInMilliwattHours is not int full || full <= 0
                || report.RemainingCapacityInMilliwattHours is not int remaining)
                return (false, "", "");
            int percent = (int)Math.Round(100.0 * remaining / full);
            int step = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
            bool charging = report.Status == BatteryStatus.Charging;
            var glyph = ((char)((charging ? 0xEBAB : 0xEBA0) + step)).ToString();
            return (true, glyph, charging ? $"%{percent} · Şarj oluyor" : $"%{percent}");
        }
        catch { return (false, "", ""); }
    }
}
