# İzekip Shell

Windows 11'in `explorer.exe` kabuğunun yerini alabilen, WinUI 3 ve C# ile yazılmış,
Fluent tasarımlı deneysel bir masaüstü kabuğu. Görev çubuğu, Başlat menüsü, arama,
denetim merkezi, saat/takvim paneli ve masaüstü simgelerinin tamamını sıfırdan
uygular — Windows 11'in birebir kopyası değil, kendi tasarım dili ve özellikleriyle.

> ⚠️ **Deneysel proje.** Şu an yalnızca *test modunda* çalışır: Windows'un kendi
> görev çubuğu ve masaüstü gizlenir, `İzekip Shell` onların yerine geçer; kapatınca
> her şey geri gelir. Gerçek oturum kabuğu (`Winlogon\Shell`) olarak kurulum henüz
> desteklenmiyor.

## Özellikler

- **Görev çubuğu** — Başlat düğmesi, arama kutusu, açık pencere listesi (gruplama,
  sağ tık menüsü, ortadaki pencere seçimi), bildirim alanı (ağ, ses, pil), saat.
  Çoklu ekranda her ekrana kendi görev çubuğu.
- **Başlat menüsü** — Sabitlenen uygulamalar, sık kullanılanlar, son açılan
  dosyalar, hızlı klasör erişimi, canlı sistem widget'ları (CPU/RAM/disk), otomatik
  kaydedilen hızlı not, pano önizlemesi.
- **Arama** — Uygulama, dosya (Windows arama dizini üzerinden), ayar sayfası ve
  web sonuçları; yerleşik hesap makinesi (`12*7+5` yazman yeterli).
- **Denetim merkezi** — Hızlı ayar kutucukları, ses kaydırıcısı, Wi-Fi ağ listesi
  ve şifreyle bağlanma.
- **Saat paneli** — Büyük saat, takvim, odak (pomodoro tarzı) zamanlayıcısı.
- **Masaüstü** — Simgeler, sürükle-bırak yerine klasik kabuk sağ tık menüsü,
  kendi oluşturduğu duvar kağıdı.
- **Özel imleç seti** — Kabukla birlikte gelen, betikle üretilen mor→mavi gradyanlı
  imleçler (`Tools/make_cursors.py`).

## Gereksinimler

- Windows 11 (10.0.19041.0 ve üzeri)
- .NET SDK 10
- Windows App SDK 2.5.x (NuGet üzerinden otomatik gelir)

## Çalıştırma

```powershell
cd IzekipShell
dotnet build -c Debug -p:Platform=x64
.\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\IzekipShell.exe
```

Kabuk açılınca Windows'un görev çubuğu ve masaüstü gizlenir. Kapatmak için:

```powershell
.\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\IzekipShell.exe --kapat
```

Bir şey ters giderse (örn. çökme), aynı klasördeki `Kurtar.cmd` dosyası Windows
arayüzünü zorla geri yükler.

## Proje yapısı

```
IzekipShell/
  App.xaml(.cs)        Kabuğun giriş noktası, pencere yaşam döngüsü
  Interop/              Win32 P/Invoke tanımları
  Models/                Görünüm modelleri (AppEntry, TaskItem, DesktopItem...)
  Services/              Shell API, pencere takibi, Wi-Fi, ses, ikon önbelleği...
  Views/                 TaskbarWindow, StartWindow, SearchWindow, PanelWindow,
                          ClockWindow, DesktopWindow
  Tools/                 İmleç ve duvar kağıdı üreten Python betikleri
```

## Katkı

Konu/PR'lar açık. Bu proje kişisel bir deney olarak başladı; kabuk davranışına
dokunan değişiklikler (özellikle kayıt defteri veya oturum kabuğu ile ilgili
olanlar) için lütfen önce bir konu (issue) açın.

## Lisans

MIT — bkz. [LICENSE](LICENSE).
