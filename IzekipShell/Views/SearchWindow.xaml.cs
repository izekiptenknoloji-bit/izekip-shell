using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using IzekipShell.Models;
using IzekipShell.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace IzekipShell.Views;

// Ayri arama deneyimi: uygulamalar, Windows arama dizinindeki dosyalar, ayarlar, web ve hesap makinesi.
public sealed partial class SearchWindow : Window
{
    const string Best = "En iyi eşleşme", Apps = "Uygulamalar", Files = "Dosyalar", Settings = "Ayarlar", Web = "Web", Calc = "Hesap makinesi";
    static readonly string[] GroupOrder = { Calc, Best, Apps, Settings, Files, Web };
    static readonly Regex MathLike = new(@"^[\d\s\.,\+\-\*/x×÷\(\)%\^]+$", RegexOptions.Compiled);

    readonly PopupController _popup;
    readonly CollectionViewSource _source;
    readonly DispatcherQueueTimer _fileTimer;
    List<AppEntry> _all = new();
    List<AppEntry> _files = new();
    string _filter = "all";
    int _queryVersion;

    public SearchWindow()
    {
        InitializeComponent();
        SystemTheme.Bind(Root);
        Title = "İzekip Arama";
        _popup = new PopupController(this, Root);
        _popup.Opened += OnOpened;
        _source = (CollectionViewSource)Root.Resources["ResultsSource"];

        // Dosya aramasi yazma durunca baslar; her tusta dizine gitmeye gerek yok.
        _fileTimer = DispatcherQueue.CreateTimer();
        _fileTimer.Interval = TimeSpan.FromMilliseconds(220);
        _fileTimer.IsRepeating = false;
        _fileTimer.Tick += (_, _) => SearchFiles(QueryBox.Text.Trim(), _queryVersion);

        BuildQuickActions();
        AppCatalog.Changed += LoadSuggestions;
        LoadSuggestions();
    }

    double Scale => Root.XamlRoot?.RasterizationScale ?? 1;

    public void Toggle() => _popup.Toggle(Rect);

    public void Open(string text = "")
    {
        if (!_popup.IsOpen) _popup.Show(Rect());
        QueryBox.Text = text;
        QueryBox.SelectionStart = text.Length;
    }

    public void ForegroundChanged(IntPtr h) => _popup.ForegroundChanged(h);

    // Gorev cubugundaki arama kutusunun hemen ustunde, genis ve uzun.
    static Windows.Graphics.RectInt32 Rect()
    {
        var bar = App.Current.Taskbar;
        var r = bar.PopupRect(1040, 760, right: false);
        int maxX = bar.Monitor.X + bar.Monitor.Width - r.Width - (int)Math.Round(12 * bar.Scale);
        r.X = Math.Clamp(bar.SearchAnchorX, bar.Monitor.X, Math.Max(bar.Monitor.X, maxX));
        return r;
    }

    void OnOpened()
    {
        QueryBox.Text = "";
        ShowHome();
        QueryBox.Focus(FocusState.Programmatic);
    }

    void LoadSuggestions()
    {
        SuggestedGrid.ItemsSource = ShellSettings.Current.StartPins
            .Select(AppCatalog.FindById).OfType<AppEntry>().Take(8).ToList();
    }

    void BuildQuickActions()
    {
        (string Text, string Glyph, string Target)[] actions =
        {
            ("Ekran alıntısı", "", "ms-screenclip:"),
            ("Dosya Gezgini", "", "explorer.exe"),
            ("Terminal", "", "wt.exe"),
            ("Görev Yöneticisi", "", "taskmgr.exe"),
            ("Wi-Fi ayarları", "", "ms-settings:network-wifi"),
            ("Windows Update", "", "ms-settings:windowsupdate"),
        };
        foreach (var (text, glyph, target) in actions)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["TaskButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 0, 12, 0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 16, Foreground = (Microsoft.UI.Xaml.Media.Brush)Root.Resources["GlowBrush"] },
                        new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            button.Click += (_, _) => { _popup.Hide(); AppCatalog.Open(target); };
            QuickActions.Children.Add(button);
        }
    }

    // ---- Sorgu ----

    void Query_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = QueryBox.Text.Trim();
        int version = ++_queryVersion;
        _fileTimer.Stop();
        _files = new List<AppEntry>();
        if (q.Length == 0) { ShowHome(); return; }

        _all = Instant(q);
        Render();
        _fileTimer.Start();
        _ = version;
    }

    List<AppEntry> Instant(string q)
    {
        var list = new List<AppEntry>();
        if (Calculate(q) is { } result)
            list.Add(new AppEntry { Name = "= " + result, Id = "calc:" + result, Glyph = "", Subtitle = q, Category = Calc });

        var compare = Format.Tr.CompareInfo;
        const CompareOptions opts = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        int Score(AppEntry a)
        {
            if (compare.IsPrefix(a.Name, q, opts)) return 0;
            int at = compare.IndexOf(a.Name, q, opts);
            if (at > 0 && !char.IsLetterOrDigit(a.Name[at - 1])) return 1;
            return at >= 0 ? 2 : -1;
        }
        IEnumerable<AppEntry> Rank(IEnumerable<AppEntry> source, int take) => source
            .Select(a => (App: a, Score: Score(a))).Where(x => x.Score >= 0)
            .OrderBy(x => x.Score).ThenBy(x => x.App.Name.Length).Take(take).Select(x => x.App);

        list.AddRange(Rank(AppCatalog.Apps, 12).Select(a => a.As(Apps)));
        list.AddRange(Rank(StartWindow.SettingsPages, 6).Select(a => a.As(Settings)));
        list.Add(new AppEntry
        {
            Name = q,
            Id = "uri:https://www.google.com/search?q=" + Uri.EscapeDataString(q),
            Glyph = "",
            Subtitle = "Google'da ara",
            Category = Web,
        });
        list.Add(new AppEntry { Name = q, Id = "run:" + q, Glyph = "", Subtitle = "Komut olarak çalıştır", Category = Web });

        // Ilk gercek eslesme "en iyi eslesme" olur.
        var best = list.FirstOrDefault(a => a.Category is Apps or Settings);
        if (best is not null && _filter == "all")
        {
            list.Remove(best);
            list.Insert(list.FirstOrDefault()?.Category == Calc ? 1 : 0, best.As(Best));
        }
        return list;
    }

    static string? Calculate(string q)
    {
        if (!MathLike.IsMatch(q) || !q.Any(char.IsDigit) || !q.Any(c => "+-*/x×÷%^".Contains(c))) return null;
        try
        {
            var expr = q.Replace(',', '.').Replace('x', '*').Replace('×', '*').Replace('÷', '/');
            var value = Convert.ToDouble(new DataTable().Compute(expr, null), CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;
            return value.ToString("#,0.##########", Format.Tr);
        }
        catch { return null; }
    }

    // Windows arama dizini: kullanici klasorlerinde ad ve icerik eslesmeleri.
    async void SearchFiles(string q, int version)
    {
        if (q.Length < 2) return;
        var roots = new[]
        {
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
        }.Select(Environment.GetFolderPath).Append(Shell.KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B")))
         .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var found = new List<AppEntry>();
        foreach (var root in roots)
        {
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(root);
                var options = new QueryOptions { FolderDepth = FolderDepth.Deep, IndexerOption = IndexerOption.UseIndexerWhenAvailable, UserSearchFilter = q };
                var items = await folder.CreateItemQueryWithOptions(options).GetItemsAsync(0, 8);
                if (version != _queryVersion) return;
                foreach (var item in items)
                {
                    found.Add(new AppEntry
                    {
                        Name = item.Name,
                        Id = "file:" + item.Path,
                        Glyph = item.IsOfType(StorageItemTypes.Folder) ? "" : "",
                        Subtitle = Path.GetDirectoryName(item.Path) ?? "",
                        Category = Files,
                    });
                }
            }
            catch { }
        }
        if (version != _queryVersion) return;
        _files = found.Take(20).ToList();
        Render();
    }

    void Render()
    {
        var selectedId = (ResultsList.SelectedItem as AppEntry)?.Id;
        var entries = _all.Concat(_files)
            .Where(a => _filter == "all" || a.Category == _filter || (a.Category == Best && Map(a) == _filter))
            .ToList();
        var groups = GroupOrder
            .Select(g => new AppGroup(g, entries.Where(e => e.Category == g)))
            .Where(g => g.Count > 0)
            .ToList();

        _source.Source = groups;
        ResultsList.ItemsSource = _source.View;
        HomePanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
        NothingFound.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var flat = groups.SelectMany(g => g).ToList();
        ResultsList.SelectedItem = flat.FirstOrDefault(e => e.Id == selectedId) ?? flat.FirstOrDefault();
        if (flat.Count == 0) ShowPreview(null);
    }

    // "En iyi eslesme" asil hangi sekmeye ait?
    static string Map(AppEntry a) => a.IsApp ? Apps : a.Id.StartsWith("file:") ? Files : Settings;

    void ShowHome()
    {
        HomePanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
        ShowPreview(null);
    }

    void Filters_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _filter = sender.SelectedItem?.Tag as string ?? "all";
        if (QueryBox.Text.Trim().Length > 0)
        {
            _all = Instant(QueryBox.Text.Trim());
            Render();
        }
    }

    // ---- Onizleme ----

    void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowPreview(ResultsList.SelectedItem as AppEntry);

    void ShowPreview(AppEntry? entry)
    {
        PreviewActions.Children.Clear();
        PreviewImage.Source = null;
        if (entry is null)
        {
            PreviewGlyph.Glyph = "";
            PreviewTitle.Text = "İzekip Arama";
            PreviewKind.Text = QueryBox.Text.Trim().Length == 0 ? "Yazmaya başla" : "Sonuç yok";
            PreviewPath.Text = "";
            return;
        }

        PreviewGlyph.Glyph = entry.Glyph;
        PreviewTitle.Text = entry.Name;
        PreviewKind.Text = entry.Category == Best ? Map(entry) : entry.Category;
        string? path = entry.Id.StartsWith("file:") ? entry.Id[5..] : entry.ExePath;
        PreviewPath.Text = path ?? (entry.Id.StartsWith("calc:") ? entry.Subtitle : "");

        if (entry.IconPath is { } iconPath)
        {
            int px = (int)Math.Round(72 * Scale);
            IconCache.Instance!.Get(iconPath, px, src =>
            {
                if (ResultsList.SelectedItem == entry) PreviewImage.Source = src;
            });
        }

        if (entry.Id.StartsWith("calc:"))
        {
            AddAction("Sonucu kopyala", "", () => CopyText(entry.Id[5..]));
            return;
        }
        AddAction(entry.Category == Web && entry.Id.StartsWith("uri:") ? "Tarayıcıda aç" : "Aç", "", () => Launch(entry));
        bool runnable = entry.ExePath is not null || (path is not null && Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".bat" or ".cmd" or ".lnk");
        if (runnable)
            AddAction("Yönetici olarak çalıştır", "", () => Launch(new AppEntry { Name = entry.Name, Id = entry.Id, ExePath = path }, admin: true));
        if (path is not null)
        {
            AddAction("Dosya konumunu aç", "", () => { _popup.Hide(); AppCatalog.Open("explorer.exe", $"/select,\"{path}\""); });
            AddAction("Yolu kopyala", "", () => CopyText(path));
        }
        if (entry.IsApp)
        {
            var s = ShellSettings.Current;
            bool onStart = s.StartPins.Contains(entry.Id), onBar = s.TaskbarPins.Contains(entry.Id);
            AddAction(onStart ? "Başlangıçtan kaldır" : "Başlangıca sabitle", onStart ? "" : "", () => { ShellSettings.Toggle(s.StartPins, entry.Id); ShowPreview(entry); });
            AddAction(onBar ? "Görev çubuğundan kaldır" : "Görev çubuğuna sabitle", onBar ? "" : "", () => { ShellSettings.Toggle(s.TaskbarPins, entry.Id); ShowPreview(entry); });
        }
    }

    void AddAction(string text, string glyph, Action action)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["TaskButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 9, 12, 9),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
            },
        };
        button.Click += (_, _) => action();
        PreviewActions.Children.Add(button);
    }

    static void CopyText(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    // ---- Girdiler ----

    void Entries_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not AppEntry entry || entry.IconRequested || entry.IconPath is not { } path) return;
        entry.IconRequested = true;
        int px = (int)Math.Round((sender == SuggestedGrid ? 32 : 28) * Scale);
        IconCache.Instance!.Get(path, px, src => entry.Icon = src);
    }

    void Entry_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AppEntry entry) Launch(entry);
    }

    void Launch(AppEntry entry, bool admin = false)
    {
        if (entry.Id.StartsWith("calc:")) { CopyText(entry.Id[5..]); return; }
        _popup.Hide();
        AppCatalog.Launch(entry, admin);
    }

    void Query_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var flat = (_source.Source as List<AppGroup>)?.SelectMany(g => g).ToList() ?? new List<AppEntry>();
        if (ResultsPanel.Visibility != Visibility.Visible || flat.Count == 0) return;
        int index = ResultsList.SelectedItem is AppEntry current ? flat.IndexOf(current) : -1;
        switch (e.Key)
        {
            case VirtualKey.Enter when index >= 0:
                Launch(flat[index]);
                break;
            case VirtualKey.Down:
                ResultsList.SelectedItem = flat[Math.Min(index + 1, flat.Count - 1)];
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                break;
            case VirtualKey.Up:
                ResultsList.SelectedItem = flat[Math.Max(index - 1, 0)];
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    void Results_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ResultsList.SelectedItem is AppEntry entry)
        {
            Launch(entry);
            e.Handled = true;
        }
    }

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        if (QueryBox.Text.Length > 0) QueryBox.Text = "";
        else _popup.Hide();
        e.Handled = true;
    }
}
