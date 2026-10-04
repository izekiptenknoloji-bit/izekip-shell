using System.Runtime.InteropServices;

namespace IzekipShell.Services;

// Varsayilan hoparlorun ana ses duzeyi (Core Audio).
public static class Audio
{
    static IAudioEndpointVolume? _endpoint;
    static Guid _context = Guid.NewGuid();

    static IAudioEndpointVolume? Endpoint()
    {
        if (_endpoint is not null) return _endpoint;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCoClass();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, 0, out var obj) != 0) return null;
            return _endpoint = (IAudioEndpointVolume)obj;
        }
        catch { return null; }
    }

    // Cihaz degisirse (kulaklik takildi) eski uc nokta gecersiz kalir: hatada bir kez yeniden al.
    static T Try<T>(Func<IAudioEndpointVolume, T> action, T fallback)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var e = Endpoint();
            if (e is null) return fallback;
            try { return action(e); }
            catch { _endpoint = null; }
        }
        return fallback;
    }

    public static (int Level, bool Muted)? Get() => Try<(int, bool)?>(e =>
    {
        Check(e.GetMasterVolumeLevelScalar(out var level));
        Check(e.GetMute(out var muted));
        return ((int)Math.Round(level * 100), muted);
    }, null);

    public static void SetLevel(int level) => Try(e =>
    {
        Check(e.SetMasterVolumeLevelScalar(Math.Clamp(level, 0, 100) / 100f, ref _context));
        if (level > 0) e.SetMute(false, ref _context);
        return true;
    }, false);

    public static void SetMuted(bool muted) => Try(e => { Check(e.SetMute(muted, ref _context)); return true; }, false);

    static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCoClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
