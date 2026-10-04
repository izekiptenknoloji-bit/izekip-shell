using IzekipShell.Interop;

namespace IzekipShell.Services;

public sealed record TaskWindow(IntPtr Hwnd, string Title, string? Exe, string? Aumid);

// Gorev cubugunda gorunmesi gereken ust duzey pencereler: Windows'un kendi gorev cubugunun
// kurallariyla ayni (gorunur, sahipsiz ya da APPWINDOW, arac penceresi degil, gizlenmemis).
public static class WindowTracker
{
    static readonly uint MyPid = (uint)Environment.ProcessId;
    static readonly Dictionary<IntPtr, (string? Exe, string? Aumid, DateTime At)> Info = new();
    static readonly List<IntPtr> MinimizedByUs = new();

    // Bizim pencerelerimiz disinda en son one gelen pencere. Masaustu tiklaninca sifirlanir.
    public static IntPtr LastActive { get; set; }

    public static List<TaskWindow> Enumerate()
    {
        var list = new List<TaskWindow>();
        var seen = new HashSet<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (IsTaskWindow(h))
            {
                seen.Add(h);
                var (exe, aumid) = Describe(h);
                list.Add(new TaskWindow(h, Native.Title(h), exe, aumid));
            }
            return true;
        }, 0);
        foreach (var gone in Info.Keys.Where(k => !seen.Contains(k)).ToList()) Info.Remove(gone);
        return list;
    }

    static bool IsTaskWindow(IntPtr h)
    {
        if (!Native.IsWindowVisible(h)) return false;
        long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
        bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if (Native.GetWindow(h, Native.GW_OWNER) != 0) return false;
            if ((ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0) return false;
        }
        if (Native.GetWindowTextLength(h) == 0) return false;
        if (Native.IsCloaked(h)) return false;
        if (Native.ProcessId(h) == MyPid) return false;
        return Native.ClassName(h) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    static (string? Exe, string? Aumid) Describe(IntPtr h)
    {
        if (Info.TryGetValue(h, out var known) && (known.Aumid is not null || DateTime.UtcNow - known.At > TimeSpan.FromSeconds(10)))
            return (known.Exe, known.Aumid);

        var exe = known.Exe ?? ExeOf(h);
        var aumid = Native.AppUserModelId(h);
        Info[h] = (exe, aumid, known.At == default ? DateTime.UtcNow : known.At);
        return (exe, aumid);
    }

    // UWP uygulamalari ApplicationFrameHost icinde calisir; asil surec cocuk penceredendir.
    static string? ExeOf(IntPtr h)
    {
        uint pid = Native.ProcessId(h);
        if (Native.ClassName(h) == "ApplicationFrameWindow")
        {
            uint child = 0;
            Native.EnumChildWindows(h, (c, _) =>
            {
                uint p = Native.ProcessId(c);
                if (p != pid) { child = p; return false; }
                return true;
            }, 0);
            if (child != 0) pid = child;
        }
        return Native.ProcessPath(pid);
    }

    public static void Activate(IntPtr h)
    {
        if (Native.IsIconic(h)) Native.ShowWindowAsync(h, Native.SW_RESTORE);
        if (!Native.SetForegroundWindow(h))
        {
            // On plana alma kilidi: bos bir Alt vurusu sureci "son girdi" sahibi yapar.
            Native.keybd_event(0x12, 0, 0, 0);
            Native.keybd_event(0x12, 0, 2, 0);
            Native.SetForegroundWindow(h);
        }
        LastActive = h;
    }

    public static void Minimize(IntPtr h)
    {
        Native.ShowWindowAsync(h, Native.SW_MINIMIZE);
        if (LastActive == h) LastActive = 0;
    }

    public static void Close(IntPtr h) => Native.PostMessage(h, Native.WM_CLOSE, 0, 0);

    // Masaustunu goster / geri al.
    public static void ToggleDesktop()
    {
        var stillDown = MinimizedByUs.Where(h => Native.IsWindow(h) && Native.IsIconic(h)).ToList();
        MinimizedByUs.Clear();
        if (stillDown.Count > 0)
        {
            for (int i = stillDown.Count - 1; i >= 0; i--) Native.ShowWindowAsync(stillDown[i], Native.SW_RESTORE);
            Activate(stillDown[0]);
            return;
        }
        foreach (var w in Enumerate())
        {
            if (Native.IsIconic(w.Hwnd)) continue;
            Native.ShowWindowAsync(w.Hwnd, Native.SW_MINIMIZE);
            MinimizedByUs.Add(w.Hwnd);
        }
        LastActive = 0;
    }
}
