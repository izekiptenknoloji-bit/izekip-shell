using System.Runtime.InteropServices;

namespace IzekipShell.Services;

// Odak zamanlayicisi: saat panelinden baslatilir, gorev cubugu saati geri sayimi gosterir.
public static class FocusTimer
{
    static DateTime? _end;
    static TimeSpan _length;

    public static event Action? Finished;

    public static TimeSpan? Remaining
    {
        get
        {
            if (_end is not { } end) return null;
            var left = end - DateTime.Now;
            if (left > TimeSpan.Zero) return left;
            _end = null;
            MessageBeep(0x40); // MB_ICONINFORMATION
            Finished?.Invoke();
            return null;
        }
    }

    // 0..1, ilerleme halkasi icin.
    public static double Progress => Remaining is { } left && _length.TotalSeconds > 0 ? 1 - left.TotalSeconds / _length.TotalSeconds : 0;

    public static void Start(TimeSpan length)
    {
        _length = length;
        _end = DateTime.Now + length;
    }

    public static void Stop() => _end = null;

    [DllImport("user32.dll")] static extern bool MessageBeep(uint type);
}
