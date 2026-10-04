using System.Globalization;

namespace IzekipShell.Services;

public static class Format
{
    public static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    static readonly string[] Units = { "bayt", "KB", "MB", "GB", "TB" };

    // Gezgin'in ayrinti gorunumu gibi: her zaman KB, yukari yuvarlanir.
    public static string KiloBytes(long bytes) => ((bytes + 1023) / 1024).ToString("N0", Tr) + " KB";

    public static string Bytes(long bytes)
    {
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{bytes} bayt" : value.ToString(value < 10 ? "0.#" : "0", Tr) + " " + Units[unit];
    }

    public static string Date(DateTime date) => date.ToString("dd.MM.yyyy HH:mm", Tr);

    public static string Count(int n) => n.ToString("N0", Tr);
}

public static class Ago
{
    public static string Of(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1) return "Az önce";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} dk önce";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} sa önce";
        if (span.TotalDays < 2) return "Dün " + time.ToString("HH:mm", Format.Tr);
        return time.ToString("d MMMM", Format.Tr);
    }
}
