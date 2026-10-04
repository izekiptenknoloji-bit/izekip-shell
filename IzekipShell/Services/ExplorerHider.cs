using System.Text.Json;
using IzekipShell.Interop;
using Microsoft.UI.Dispatching;

namespace IzekipShell.Services;

// Test modu: explorer.exe calismaya devam eder, yalniz gorev cubugu ve masaustu penceresi gizlenir.
// Windows gorev cubugu "otomatik gizle"ye alinir ki ekranin altini birakip yeri bize versin.
// Ne yapildigi diske yazilir; kabuk cokerse bir sonraki acilista ya da Kurtar.cmd ile geri alinir.
public static class ExplorerHider
{
    sealed class State
    {
        public long TrayState { get; set; }
        public bool HasTray { get; set; }
        public List<long> Hidden { get; set; } = new();
    }

    static readonly string StateFile = AppPaths.File("gizlenenler.json");
    static readonly object Gate = new();
    static State? _state;
    static DispatcherQueueTimer? _timer;

    public static void Hide(DispatcherQueue ui)
    {
        lock (Gate)
        {
            var state = new State();
            var tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray != 0)
            {
                var abd = Native.AppBar(tray);
                state.TrayState = (long)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd);
                state.HasTray = true;
                abd.lParam = Native.ABS_AUTOHIDE;
                Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
            }
            _state = state;
            HideTargets();
        }

        // Explorer bazen kendi pencerelerini geri acar (ornegin yeniden baslayinca).
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => { lock (Gate) if (_state is not null) HideTargets(); };
        _timer.Start();
    }

    static void HideTargets()
    {
        bool changed = false;
        foreach (var h in Targets())
        {
            if (!Native.IsWindowVisible(h)) continue;
            Native.ShowWindow(h, Native.SW_HIDE);
            if (!_state!.Hidden.Contains(h)) { _state.Hidden.Add(h); changed = true; }
        }
        if (changed || !File.Exists(StateFile)) Save(_state!);
    }

    static List<IntPtr> Targets()
    {
        var list = new List<IntPtr>();
        var progman = Native.FindWindow("Progman", null);
        uint explorer = progman != 0 ? Native.ProcessId(progman) : 0;
        Native.EnumWindows((h, _) =>
        {
            var cls = Native.ClassName(h);
            if (cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman") list.Add(h);
            else if (cls == "WorkerW" && explorer != 0 && Native.ProcessId(h) == explorer) list.Add(h);
            return true;
        }, 0);
        return list;
    }

    // Her yerden cagrilabilir (cikis, cokme). Iki kez cagrilmasi zararsiz.
    public static void Restore()
    {
        lock (Gate)
        {
            _timer?.Stop();
            if (_state is null) return;
            Apply(_state);
            _state = null;
            try { File.Delete(StateFile); } catch { }
        }
    }

    // Onceki oturum duzgun kapanmadiysa Windows arayuzunu geri getirir.
    public static void RestoreStale()
    {
        lock (Gate)
        {
            if (!File.Exists(StateFile)) return;
            try
            {
                var state = JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile));
                if (state is not null) Apply(state);
            }
            catch { }
            // Pencere tutamaclari degismis olabilir: explorer'in butun kabuk pencerelerini ac.
            foreach (var h in Targets())
                if (Native.ClassName(h) != "WorkerW") Native.ShowWindow(h, Native.SW_SHOWNA);
            try { File.Delete(StateFile); } catch { }
        }
    }

    static void Apply(State state)
    {
        if (state.HasTray)
        {
            var tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray != 0)
            {
                var abd = Native.AppBar(tray);
                abd.lParam = (IntPtr)state.TrayState;
                Native.SHAppBarMessage(Native.ABM_SETSTATE, ref abd);
            }
        }
        foreach (var h in state.Hidden)
            if (Native.IsWindow((IntPtr)h)) Native.ShowWindow((IntPtr)h, Native.SW_SHOWNA);
    }

    static void Save(State state)
    {
        try { File.WriteAllText(StateFile, JsonSerializer.Serialize(state)); }
        catch { }
    }
}
