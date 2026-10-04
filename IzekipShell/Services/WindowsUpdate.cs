using Microsoft.Win32;

namespace IzekipShell.Services;

public enum UpdateState { Unknown, UpToDate, Checking, Available, RebootRequired, Error }

// Windows Update durumu: Windows Update Agent (WUA) COM API'si ile bekleyen guncelleme
// sayisi, yeniden baslatma kaydi (RebootRequired) ile de bekleyen yeniden baslatma.
// Arama internet gerektirir ve saniyeler surebilir; bu yuzden arka planda, seyrek ve
// tek seferde bir calisir, sonuc bellekte tutulur.
public static class WindowsUpdate
{
    public static UpdateState State { get; private set; } = UpdateState.Unknown;
    public static int PendingCount { get; private set; }
    public static DateTime? LastChecked { get; private set; }
    public static event Action? Changed;

    static bool _checking;

    public static string Summary => State switch
    {
        UpdateState.Checking => "Güncellemeler denetleniyor…",
        UpdateState.Available => PendingCount == 1 ? "1 güncelleme hazır" : $"{PendingCount} güncelleme hazır",
        UpdateState.RebootRequired => "Yeniden başlatma bekliyor",
        UpdateState.UpToDate => "Windows güncel",
        UpdateState.Error => "Durum denetlenemedi",
        _ => "Henüz denetlenmedi",
    };

    // Her zaman aninda: kayit defterinden okunur.
    public static bool RebootPending()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            return key is not null;
        }
        catch { return false; }
    }

    public static void RefreshAsync()
    {
        if (_checking) return;
        _checking = true;
        State = UpdateState.Checking;
        Changed?.Invoke();

        var thread = new Thread(() =>
        {
            try { Search(); }
            catch { State = UpdateState.Error; }
            finally
            {
                LastChecked = DateTime.Now;
                _checking = false;
                Changed?.Invoke();
            }
        }) { IsBackground = true, Name = "WindowsUpdateCheck" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    static void Search()
    {
        if (RebootPending()) { State = UpdateState.RebootRequired; PendingCount = 0; return; }

        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
        if (sessionType is null) { State = UpdateState.Error; return; }
        dynamic session = Activator.CreateInstance(sessionType)!;
        dynamic searcher = session.CreateUpdateSearcher();
        // Yuklu/gizli olmayan, kurulmamis guncellemeler: tipki Windows Update sayfasindaki liste.
        dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
        int count = result.Updates.Count;

        PendingCount = count;
        State = count > 0 ? UpdateState.Available : UpdateState.UpToDate;
    }
}
