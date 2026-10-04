using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace IzekipShell.Services;

public sealed record WifiNetwork(string Ssid, int Signal, bool Secured, bool Connected, bool HasProfile, string? ProfileName, int Auth)
{
    // Signal 0-100 -> Segoe Fluent cubuk glifi.
    public string Glyph => Signal >= 75 ? "" : Signal >= 50 ? "" : Signal >= 25 ? "" : "";
    public string Status => Connected ? "Bağlı" + (Secured ? ", güvenli" : "") : Secured ? "Güvenli" : "Açık ağ";
    public bool IsEnterprise => Auth is 3 or 6 or 8;
    public bool NeedsPassword => Secured && !HasProfile;
}

// Windows'un yerel Wi-Fi API'si (wlanapi). Dil bagimsizdir; netsh ciktisini ayristirmaya gerek yok.
public static class Wifi
{
    const uint FlagConnected = 1, FlagHasProfile = 2;

    public static Guid? Interface()
    {
        if (!Open(out var h)) return null;
        try
        {
            if (WlanEnumInterfaces(h, 0, out var list) != 0) return null;
            try
            {
                int count = Marshal.ReadInt32(list);
                if (count == 0) return null;
                return Marshal.PtrToStructure<Guid>(list + 8);
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(h, 0); }
    }

    // Tarama yaklasik 4 saniyede tamamlanir; liste hemen eski sonuclarla doner.
    public static void Scan()
    {
        if (Interface() is not { } id || !Open(out var h)) return;
        try { WlanScan(h, ref id, 0, 0, 0); }
        finally { WlanCloseHandle(h, 0); }
    }

    public static List<WifiNetwork> Networks()
    {
        var result = new List<WifiNetwork>();
        if (Interface() is not { } id || !Open(out var h)) return result;
        try
        {
            if (WlanGetAvailableNetworkList(h, ref id, 0, 0, out var list) != 0) return result;
            try
            {
                int count = Marshal.ReadInt32(list);
                int size = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
                for (int i = 0; i < count; i++)
                {
                    var n = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(list + 8 + i * size);
                    var ssid = Encoding.UTF8.GetString(n.dot11Ssid.ucSSID, 0, (int)Math.Min(n.dot11Ssid.uSSIDLength, 32u));
                    if (ssid.Length == 0) continue; // gizli ag
                    result.Add(new WifiNetwork(ssid, (int)n.wlanSignalQuality, n.bSecurityEnabled,
                        (n.dwFlags & FlagConnected) != 0, (n.dwFlags & FlagHasProfile) != 0,
                        string.IsNullOrEmpty(n.strProfileName) ? null : n.strProfileName, n.dot11DefaultAuthAlgorithm));
                }
            }
            finally { WlanFreeMemory(list); }
        }
        finally { WlanCloseHandle(h, 0); }

        // Ayni SSID birden cok kez gelir (profilli/profilsiz, 2.4/5 GHz): en iyisini tut.
        return result
            .GroupBy(n => n.Ssid)
            .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.HasProfile).ThenByDescending(n => n.Signal).First())
            .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal)
            .ToList();
    }

    // Kayitli profil varsa onunla, yoksa sifreden profil olusturup baglanir. Hata metni ya da null doner.
    public static async Task<string?> ConnectAsync(WifiNetwork network, string? password)
    {
        if (network.IsEnterprise) return "Kurumsal ağlara Windows Ayarlar'dan bağlanın.";
        if (Interface() is not { } id) return "Wi-Fi bağdaştırıcısı bulunamadı.";

        string profile = network.ProfileName ?? network.Ssid;
        bool created = false;
        if (!network.HasProfile)
        {
            if (network.Secured && string.IsNullOrEmpty(password)) return "Şifre gerekli.";
            var error = await Task.Run(() => SetProfile(id, ProfileXml(network, password)));
            if (error is not null) return error;
            profile = network.Ssid;
            created = true;
        }

        var connectError = await Task.Run(() => Connect(id, profile));
        if (connectError is not null) return connectError;

        // Baglanti dogrulanana kadar bekle (yanlis sifre burada anlasilir).
        for (int i = 0; i < 24; i++)
        {
            await Task.Delay(500);
            var now = await Task.Run(Networks);
            if (now.Any(n => n.Ssid == network.Ssid && n.Connected)) return null;
        }
        if (created) await Task.Run(() => DeleteProfile(id, profile));
        return created ? "Bağlanılamadı. Şifre yanlış olabilir." : "Bağlanılamadı.";
    }

    public static void Disconnect()
    {
        if (Interface() is not { } id || !Open(out var h)) return;
        try { WlanDisconnect(h, ref id, 0); }
        finally { WlanCloseHandle(h, 0); }
    }

    static string? SetProfile(Guid id, string xml)
    {
        if (!Open(out var h)) return "Wi-Fi hizmetine ulaşılamadı.";
        try
        {
            int rc = WlanSetProfile(h, ref id, 0, xml, null, true, 0, out _);
            return rc == 0 ? null : $"Profil kaydedilemedi (kod {rc}).";
        }
        finally { WlanCloseHandle(h, 0); }
    }

    static string? Connect(Guid id, string profile)
    {
        if (!Open(out var h)) return "Wi-Fi hizmetine ulaşılamadı.";
        try
        {
            var p = new WLAN_CONNECTION_PARAMETERS { wlanConnectionMode = 0, strProfile = profile, dot11BssType = 1 };
            int rc = WlanConnect(h, ref id, ref p, 0);
            return rc == 0 ? null : $"Bağlantı başlatılamadı (kod {rc}).";
        }
        finally { WlanCloseHandle(h, 0); }
    }

    static void DeleteProfile(Guid id, string profile)
    {
        if (!Open(out var h)) return;
        try { WlanDeleteProfile(h, ref id, profile, 0); }
        finally { WlanCloseHandle(h, 0); }
    }

    static string ProfileXml(WifiNetwork n, string? password)
    {
        var name = SecurityElement.Escape(n.Ssid);
        var hex = Convert.ToHexString(Encoding.UTF8.GetBytes(n.Ssid));
        var (auth, cipher) = n.Auth switch
        {
            1 => ("open", "none"),
            4 => ("WPAPSK", "AES"),
            9 or 10 => ("WPA3SAE", "AES"),
            _ => ("WPA2PSK", "AES"),
        };
        var key = n.Secured
            ? $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>"
            : "";
        return $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>{name}</name>
              <SSIDConfig><SSID><hex>{hex}</hex><name>{name}</name></SSID></SSIDConfig>
              <connectionType>ESS</connectionType>
              <connectionMode>auto</connectionMode>
              <MSM><security>
                <authEncryption><authentication>{auth}</authentication><encryption>{cipher}</encryption><useOneX>false</useOneX></authEncryption>
                {key}
              </security></MSM>
            </WLANProfile>
            """;
    }

    static bool Open(out IntPtr handle) => WlanOpenHandle(2, 0, out _, out handle) == 0;

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    struct DOT11_SSID
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WLAN_AVAILABLE_NETWORK
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strProfileName;
        public DOT11_SSID dot11Ssid;
        public int dot11BssType;
        public uint uNumberOfBssids;
        [MarshalAs(UnmanagedType.Bool)] public bool bNetworkConnectable;
        public uint wlanNotConnectableReason;
        public uint uNumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public int[] dot11PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool bMorePhyTypes;
        public uint wlanSignalQuality;
        [MarshalAs(UnmanagedType.Bool)] public bool bSecurityEnabled;
        public int dot11DefaultAuthAlgorithm;
        public int dot11DefaultCipherAlgorithm;
        public uint dwFlags;
        public uint dwReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WLAN_CONNECTION_PARAMETERS
    {
        public int wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)] public string strProfile;
        public IntPtr pDot11Ssid;
        public IntPtr pDesiredBssidList;
        public int dot11BssType;
        public uint dwFlags;
    }

    [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, string xml, string? security, bool overwrite, IntPtr reserved, out uint reason);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern int WlanDeleteProfile(IntPtr handle, ref Guid iface, string profile, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanConnect(IntPtr handle, ref Guid iface, ref WLAN_CONNECTION_PARAMETERS p, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern int WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr memory);
}
