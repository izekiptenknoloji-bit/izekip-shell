using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace IzekipShell.Services;

// Windows kabugunun (shell32) kendisi. Simge, tur adi ve dosya islemleri buradan gecer;
// boylece sistemdeki simge ayarlari ve Gezgin'in geri alma gecmisi bizde de gecerli.
public static class Shell
{
    // ---- Ad ve tur ----

    static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string, bool> HiddenExtensions = new(StringComparer.OrdinalIgnoreCase);
    static readonly bool HideKnownExtensions =
        Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced")?.GetValue("HideFileExt") is not int hide || hide == 1;

    public static string TypeName(string extension) => TypeNames.GetOrAdd(extension, ext =>
    {
        var info = new SHFILEINFO();
        SHGetFileInfo(ext.Length == 0 ? "dosya" : "x" + ext, FILE_ATTRIBUTE_NORMAL, ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
        return string.IsNullOrEmpty(info.szTypeName) ? ext.TrimStart('.').ToUpperInvariant() + " Dosyası" : info.szTypeName;
    });

    public static string? DisplayName(string path)
    {
        var info = new SHFILEINFO();
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_DISPLAYNAME) == IntPtr.Zero)
            return null;
        return string.IsNullOrEmpty(info.szDisplayName) ? null : info.szDisplayName;
    }

    // Gezgin gibi: kayitli turlerin uzantisi gizlenir, .lnk/.url hic gosterilmez.
    public static bool HidesExtension(string extension) => extension.Length > 0 && HiddenExtensions.GetOrAdd(extension, ext =>
    {
        using var key = Registry.ClassesRoot.OpenSubKey(ext);
        if (key is null) return false;
        if (key.GetValue("NeverShowExt") is not null) return true;
        if (key.GetValue(null) is string progId)
        {
            using var prog = Registry.ClassesRoot.OpenSubKey(progId);
            if (prog?.GetValue("NeverShowExt") is not null) return true;
            if (prog?.GetValue("AlwaysShowExt") is not null) return false;
        }
        return HideKnownExtensions;
    });

    public static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    public static string? ShortcutTarget(string lnk)
    {
        var link = (IShellLinkW)new ShellLinkCoClass();
        try
        {
            ((IPersistFile)link).Load(lnk, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
        finally { Marshal.ReleaseComObject(link); }
    }

    public static int Compare(string a, string b) => StrCmpLogicalW(a, b);

    // ---- Simge ----

    // BGRA, on-carpimli alfa: WriteableBitmap'in bekledigi bicim.
    public static (byte[] Pixels, int Width, int Height)? IconPixels(string path, int px)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            if (factory.GetImage(new SIZE { cx = px, cy = px }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out var hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            try { return Pixels(hbm); }
            finally { DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    static (byte[], int, int)? Pixels(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
        };
        var pixels = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, pixels, ref header, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        // Alfasiz eski simgeler: tamamen opak say.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
        if (!hasAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return (pixels, w, h);
        }

        // Kabuk duz (on-carpimsiz) alfa verir; WriteableBitmap on-carpimli bekler.
        // Carpilmazsa yari saydam kenarlar beyaz hale olarak gorunur.
        bool straight = false;
        for (int i = 0; i < pixels.Length && !straight; i += 4)
        {
            byte a = pixels[i + 3];
            straight = pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a;
        }
        if (straight)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }
        }
        return (pixels, w, h);
    }

    // ---- Dosya islemleri ----
    // SHFileOperation kendi ilerleme/cakisma diyaloglarini gosterir ve Gezgin'in geri alma
    // gecmisine yazar. Kendi STA is parcaciginda calisir ki arayuz donmasin.

    public static Task<bool> CopyAsync(IReadOnlyList<string> sources, string targetDir, bool move, IntPtr owner) =>
        FileOperation(owner, move ? FO_MOVE : FO_COPY, sources, targetDir,
            FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOF_RENAMEONCOLLISION);

    public static Task<bool> DeleteAsync(IReadOnlyList<string> paths, bool permanent, IntPtr owner) =>
        FileOperation(owner, FO_DELETE, paths, null, permanent ? (ushort)0 : (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING));

    public static Task<bool> RenameAsync(string path, string newPath, IntPtr owner) =>
        FileOperation(owner, FO_RENAME, new[] { path }, newPath, FOF_ALLOWUNDO);

    static Task<bool> FileOperation(IntPtr owner, uint func, IReadOnlyList<string> from, string? to, ushort flags)
    {
        var done = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    hwnd = owner,
                    wFunc = func,
                    pFrom = string.Join('\0', from) + "\0\0",
                    pTo = to is null ? null : to + "\0\0",
                    fFlags = flags,
                };
                int result = SHFileOperation(ref op);
                done.SetResult(result == 0 && op.fAnyOperationsAborted == 0);
            }
            catch (Exception e) { done.SetException(e); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return done.Task;
    }

    public static void ShowProperties(IntPtr owner, string path) => SHObjectProperties(owner, SHOP_FILEPATH, path, null);

    // ---- P/Invoke ----

    const uint SHGFI_DISPLAYNAME = 0x200, SHGFI_TYPENAME = 0x400, SHGFI_USEFILEATTRIBUTES = 0x10;
    const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    const int SIIGBF_BIGGERSIZEOK = 0x1, SIIGBF_ICONONLY = 0x4;
    const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3, FO_RENAME = 4;
    const ushort FOF_RENAMEONCOLLISION = 0x8, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200, FOF_WANTNUKEWARNING = 0x4000;
    const uint SHOP_FILEPATH = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLinkCoClass { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int c, out BITMAP bm);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
