using System.Runtime.InteropServices;

namespace IzekipShell.Services;

// Baslat'taki sistem widget'i: islemci, bellek ve sistem diski dolulugu.
public static class SystemStats
{
    static ulong _lastIdle, _lastTotal;

    // Iki cagri arasindaki ortalama islemci kullanimi (0-100).
    public static int Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong i = idle.Value, total = kernel.Value + user.Value; // kernel suresi bosta gecen sureyi de icerir
        ulong di = i - _lastIdle, dt = total - _lastTotal;
        bool first = _lastTotal == 0;
        _lastIdle = i;
        _lastTotal = total;
        return first || dt == 0 ? 0 : (int)Math.Round(100.0 * (dt - di) / dt);
    }

    public static (int Percent, string Text) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, "");
        long used = (long)(m.ullTotalPhys - m.ullAvailPhys);
        return ((int)m.dwMemoryLoad, $"{Format.Bytes(used)} / {Format.Bytes((long)m.ullTotalPhys)}");
    }

    public static (int Percent, string Drive) SystemDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var d = new DriveInfo(root);
            return ((int)Math.Round(100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize), root.TrimEnd('\\'));
        }
        catch { return (0, "C:"); }
    }

    public static string Uptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} gün {t.Hours} saattir açık" : $"{t.Hours} sa {t.Minutes} dk'dır açık";
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME64 { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME64 idle, out FILETIME64 kernel, out FILETIME64 user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
