using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace IzekipShell.Services;

// Simgeler tek bir STA is parcaciginda kabuktan cikarilir; sonuc UI'da WriteableBitmap olur.
// Yol "shell:AppsFolder\<kimlik>" de olabilir: Baslat'taki uygulamalarin simgesi boyle gelir.
public sealed class IconCache
{
    public static IconCache? Instance { get; set; }

    readonly DispatcherQueue _ui;
    readonly BlockingCollection<(string Key, string Path, int Px)> _jobs = new();
    readonly Dictionary<string, ImageSource?> _done = new();
    readonly Dictionary<string, List<Action<ImageSource?>>> _waiting = new();

    public IconCache(DispatcherQueue ui)
    {
        _ui = ui;
        var thread = new Thread(Work) { IsBackground = true, Name = "IconCache" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    // Yalniz UI is parcacigindan cagrilir.
    public void Get(string path, int px, Action<ImageSource?> done)
    {
        var key = path + "|" + px;
        if (_done.TryGetValue(key, out var ready)) { done(ready); return; }
        if (_waiting.TryGetValue(key, out var list)) { list.Add(done); return; }
        _waiting[key] = new List<Action<ImageSource?>> { done };
        _jobs.Add((key, path, px));
    }

    // Once ilk yol denenir, simge cikmazsa ikincisi.
    public void Get(string path, string? fallback, int px, Action<ImageSource?> done) =>
        Get(path, px, src =>
        {
            if (src is null && fallback is not null && fallback != path) Get(fallback, px, done);
            else done(src);
        });

    public void Forget(string path, int px) => _done.Remove(path + "|" + px);

    void Work()
    {
        foreach (var (key, path, px) in _jobs.GetConsumingEnumerable())
        {
            (byte[] Pixels, int Width, int Height)? result = null;
            try { result = Shell.IconPixels(path, px); }
            catch { }
            _ui.TryEnqueue(() => Complete(key, result));
        }
    }

    void Complete(string key, (byte[] Pixels, int Width, int Height)? result)
    {
        ImageSource? source = null;
        if (result is { } r)
        {
            var bitmap = new WriteableBitmap(r.Width, r.Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
                stream.Write(r.Pixels, 0, r.Pixels.Length);
            bitmap.Invalidate();
            source = bitmap;
        }
        _done[key] = source;
        if (_waiting.Remove(key, out var callbacks))
            foreach (var callback in callbacks) callback(source);
    }
}
