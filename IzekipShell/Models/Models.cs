using System.ComponentModel;
using System.Runtime.CompilerServices;
using IzekipShell.Services;
using Microsoft.UI.Xaml.Media;

namespace IzekipShell.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Baslat'taki bir girdi. Kimlik onekleri: yok = shell:AppsFolder uygulamasi,
// "uri:" = ayar sayfasi/web, "run:" = komut, "file:" = son kullanilan dosya.
public sealed class AppEntry : Observable
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public string? ExePath { get; init; }
    public string Glyph { get; init; } = "";
    public string Subtitle { get; init; } = "";
    // Aramada hangi baslik altinda gosterilecegi.
    public string Category { get; init; } = "";

    public AppEntry As(string category) => new()
    {
        Name = Name, Id = Id, ExePath = ExePath, Glyph = Glyph, Subtitle = Subtitle, Category = category, Icon = Icon,
    };

    public bool IsApp => !Id.StartsWith("uri:") && !Id.StartsWith("run:") && !Id.StartsWith("file:") && !Id.StartsWith("calc:");
    public bool HasSubtitle => Subtitle.Length > 0;

    // Simgenin cikarilacagi kabuk yolu; yoksa yalniz glif gosterilir.
    public string? IconPath => IsApp ? @"shell:AppsFolder\" + Id : Id.StartsWith("file:") ? Id[5..] : null;

    public string Letter
    {
        get
        {
            var c = Name.TrimStart().FirstOrDefault();
            return char.IsLetter(c) ? char.ToUpper(c, Format.Tr).ToString() : "#";
        }
    }

    ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { if (Set(ref _icon, value)) Raise(nameof(HasNoIcon)); }
    }
    public bool HasNoIcon => _icon is null;
    internal bool IconRequested;

    public override string ToString() => Name;
}

public sealed class AppGroup : List<AppEntry>
{
    public AppGroup(string key, IEnumerable<AppEntry> items) : base(items) => Key = key;
    public string Key { get; }
    public override string ToString() => Key;
}

// Gorev cubugu dugmesi: sabitlenmis bir uygulama ve/veya ayni uygulamanin acik pencereleri.
public sealed class TaskItem : Observable
{
    public TaskItem(string key) => Key = key;

    public string Key { get; }
    public AppEntry? App { get; set; }
    public bool IsPinned { get; set; }
    public List<TaskWindow> Windows { get; } = new();
    internal string? IconFor;

    public string Name => App?.Name ?? Windows.FirstOrDefault()?.Title ?? "";

    ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    bool _isRunning, _isActive;
    public bool IsRunning
    {
        get => _isRunning;
        set { if (Set(ref _isRunning, value)) Raise(nameof(ShowIdleIndicator)); }
    }
    public bool IsActive
    {
        get => _isActive;
        set { if (Set(ref _isActive, value)) Raise(nameof(ShowIdleIndicator)); }
    }
    public bool ShowIdleIndicator => _isRunning && !_isActive;

    string _tooltip = "";
    public string Tooltip { get => _tooltip; set => Set(ref _tooltip, value); }

    public bool Matches(TaskWindow w) =>
        App is not null &&
        ((w.Aumid is not null && string.Equals(w.Aumid, App.Id, StringComparison.OrdinalIgnoreCase)) ||
         (App.ExePath is not null && w.Exe is not null && string.Equals(App.ExePath, w.Exe, StringComparison.OrdinalIgnoreCase)));

    public override string ToString() => Name;
}

public sealed class DesktopItem : Observable
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsFolder { get; init; }
    // Ozel ogeler (Bu bilgisayar, Geri Donusum Kutusu) kabuk yoluyla acilir.
    public bool IsSpecial { get; init; }

    ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    public override string ToString() => Name;
}
