# KolayKur yeni tasarım: tasarım belgesi

8 Kasım 2026 · Durum: proje sahibi sohbette onayladı · Görsel taslaklar: claude.ai'da özel "KolayKur Tasarım Taslakları" sayfası (B yönü ve logo 2)

## Amaç

Program şu an WPF'in en sade varsayılan kontrolleriyle duruyor: renk, ikon ve tema yok. Hedef, Windows 11'e yakışan, özenli ve tanınabilir bir görünüm. İşlevler değişmez. Kullanıcılar proje sahibi ve teknik olmayan arkadaşlarıdır.

**Başarı ölçütleri**

- İlk bakışta modern bir Windows 11 uygulaması gibi görünür, açık ve koyu temada.
- Var olan bütün işlevler aynen çalışır; mevcut otomatik testler değişmeden geçer.
- Program yine tek `KolayKur.exe` dosyasıdır.
- Her metin `Strings.resx` / `Strings.en.resx` dosyalarından gelir, iki dil eşit kalır.

## Kapsam

**İçinde:** pencere ve tema, logo ve uygulama ikonu, sol menü, İşlemler paneli, beş sayfanın (Ara, Güncellemeler, Yüklü uygulamalar, Ayarlar, Onarım) yeni görünümü, tema seçimi, güncelleme sayısı rozeti, "Bitenleri temizle", uygulama harf avatarları, sürüm bilgisi.

**Dışında:** uygulamaların gerçek ikonları (winget vermiyor), animasyonlu sayfa geçişleri, yeni işlevler, Windows'un vurgu rengini izleme.

## Görsel kimlik

### Logo

Logo 2: yuvarlak köşeli camgöbeği kare, içinde beyaz kalın "K", sağ alt köşede sarı daire içinde koyu onay işareti. 64×64 tasarım ızgarasında:

- Zemin: `rect x=4 y=4 w=56 h=56 rx=14`, renk `#0E7C7B`
- K gövdesi: `M23 16 V48`, beyaz, kalınlık 7, yuvarlak uç
- K kolları: `M42 16 L26 32 L42 48`, beyaz, kalınlık 7, yuvarlak uç ve birleşim
- Rozet: `circle cx=46 cy=46 r=12`, dolgu `#FFB020`, çizgi `#0E7C7B` kalınlık 3
- Onay: `M40.5 46.5 L44.5 50.5 L51.5 42.5`, renk `#1B1B1B`, kalınlık 3.5

**Küçük boyut:** 24 px ve altında rozet okunmaz. 16, 20 ve 24 px ikonlarda rozet çıkarılır, yalnız K kalır.

**Kullanıldığı yerler:** `.exe` ikonu, görev çubuğu, pencere başlığı (18 px), Ayarlar › Hakkında (28 px).

### Renkler

Marka rengi camgöbeğidir; Windows'un vurgu rengi kullanılmaz.

| Belirteç | Açık tema | Koyu tema | Nerede |
| --- | --- | --- | --- |
| Vurgu | `#0E7C7B` | `#3DD6C6` | Birincil düğmeler, seçili menü çizgisi, rozetler, ilerleme çubuğu |
| Vurgu üstü yazı | `#FFFFFF` | `#000000` | Birincil düğme yazısı |
| Pencere zemini (Mica yoksa) | `#F3F3F3` | `#202020` | Başlık, menü |
| İçerik katmanı | `#F9F9F9` | `#272727` | Sayfa alanı, İşlemler paneli |
| Kart | `#FFFFFF` | `#2D2D2D` | Kartlar |
| Kart çizgisi | `#E5E5E5` | `#393939` | Kart kenarı |
| Yazı | `#1A1A1A` | `#FFFFFF` | Ana metin |
| İkincil yazı | `#5C5C5C` | `#C8C8C8` | Yayıncı, sürüm, açıklamalar |
| Başarı | `#0F7B0F` | `#6CCB5F` | "Kurulu", "Bitti" |
| Uyarı | `#9D5D00` | `#FCE100` | "Bulunamadı" |

Bütün yazılar zeminlerine karşı en az 4.5:1 karşıtlık sağlar. WPF-UI'ın kendi tema renkleri bu tablo dışındaki her yerde kullanılır.

### Tema

- Seçenekler: **Sistem** (varsayılan), **Açık**, **Koyu**. Ayarlar sayfasında seçilir, `settings.json`'a yazılır.
- Sistem modunda Windows teması değişince program da anında değişir.
- Seçim program açılırken, pencere görünmeden önce uygulanır (açılışta yanlış temada parlama olmaz).

### Yazı tipi ve ikonlar

- Segoe UI Variable (Windows'un kendi yazı tipi). Sayfa başlıkları 28 px yarı kalın.
- İkonlar WPF-UI'ın Fluent sembolleridir (`SymbolRegular`).

## Düzen

### Pencere

- WPF-UI `FluentWindow`: Windows 11'de Mica arka planı, başlık çubuğu pencereye gömülü. Mica desteklenmeyen sistemde düz zemin.
- Başlık çubuğunda solda logo (18 px) ve "KolayKur", sağda **İşlemler** düğmesi ve pencere düğmeleri.
- Varsayılan boyut 1100×720, en küçük 760×520.

### Sol menü

- Dar menü (80 px): her öğede ikon ve altında kısa yazı: **Ara**, **Güncelle**, **Yüklü**, en altta **Ayarlar**.
- Seçili öğe: hafif dolgu ve solda 3 px vurgu çizgisi.
- "Güncelle" öğesinde güncelleme sayısı rozeti (0 veya bilinmiyorsa görünmez).
- Menü, bugünkü `ShellViewModel.CurrentPage` gezinmesini kullanır. WPF-UI'ın `NavigationView` ve sayfa gezinmesi kullanılmaz; böylece "winget bozuksa önce onarım" akışı ve `IsChecking` davranışı aynen kalır.

### İşlemler paneli

- Sağda 300 px genişliğinde panel. İçinde her iş bir kart: uygulama adı, durum (yerelleştirilmiş), açıklama, sürüyorsa ilerleme çubuğu. Teknik ayrıntı ipucu (tooltip) olarak durur.
- Bir iş kuyruğa eklenince panel kendiliğinden açılır. Kullanıcı panelin X düğmesiyle veya başlıktaki **İşlemler** düğmesiyle kapatıp açabilir.
- Panel kapalıyken başlıktaki **İşlemler** düğmesinde süren ve sıradaki iş sayısı rozeti görünür.
- **Bitenleri temizle**: başarılı, başarısız, iptal ve bulunamadı durumundaki işleri listeden kaldırır; sürenler ve sıradakiler kalır.
- Altta son toplu işin özeti (bugünkü `SummaryText`).

### Ortak bileşenler

- **Uygulama kartı:** harf avatarı (40 px), ad, yayıncı, sürüm etiketi ve sayfaya göre eylem. Kartlar akan bir ızgarada dizilir (en az 220 px genişlik).
- **Harf avatarı:** winget uygulama ikonu vermediği için her uygulamaya adının ilk harfi ya da rakamı ve kimliğinden hesaplanan sabit bir renk verilir. Aynı uygulama her zaman aynı rengi alır. 8 renklik palet, beyaz harfle en az 4.5:1 karşıtlıkta.
- **Bilgi çubuğu:** bugünkü `ErrorBanner` yerine WPF-UI `InfoBar` (hata önemi). "Tekrar dene" düğmesi yalnız yeniden denenebilir hatalarda görünür (katalog/internet hatası); liste dosyası hatalarında görünmez.
- **Düğmeler:** birincil (vurgu renkli) düğmeler: Ara, Kur, Tümünü güncelle, Listeden kur ve onarım sayfasındaki Onar. Diğer bütün düğmeler ikincildir (Seçilenleri kur, Güncelle, Kaldır, Listeyi dışa aktar, Ayarlar'daki düğmeler).

## Sayfalar

**Ara:** başlık, arama kutusu ve "Ara" düğmesi (Enter de arar), sonuç sayısı, "Seçilenleri kur (N)". Sonuçlar kart: sağ üstte seçim kutusu, seçili kartın kenarı vurgu renginde. Kurulu olanlarda yeşil "Kurulu" etiketi, olmayanlarda birincil "Kur" düğmesi. Arama sürerken yükleniyor göstergesi.

**Güncellemeler:** başlık, "N güncelleme var", birincil "Tümünü güncelle". Kartlarda "kurulu sürüm → yeni sürüm" ve ikincil "Güncelle". Güncelleme yoksa sade bir boş durum metni.

**Yüklü uygulamalar:** başlık, "N uygulama", "Listeyi dışa aktar" (ikincil) ve "Listeden kur" (birincil). Kartlarda ikincil "Kaldır" (bugünkü onay sorusu aynen). Dışa aktarma kuralları (boş/yarım liste yazılmaz, sayı bildirimi) aynen kalır.

**Ayarlar:** satır kartları: **Tema** (Sistem/Açık/Koyu seçimi), **Dil** (Türkçe/English), **Winget durumu** (durum metni ve "Onar"), **Günlük dosyası** ("Günlüğü aç"), **Hakkında** (logo, "KolayKur", "Sürüm 0.1.0").

**Onarım:** bugünkü akış aynen; görünüm aynı dilde: ortalanmış kart, durum metni, ilerleme, birincil "Onar", hata bilgi çubuğu.

**Açılış denetimi:** winget denetlenirken sayfa içeriği yine gizli kalır; ortada ilerleme halkası ve "Winget denetleniyor..." metni.

## Teknik yapı

### Kütüphane

- **WPF-UI 4.3.0** (`WPF-UI` NuGet paketi, MIT lisansı) yalnız `KolayKur.App` projesine eklenir. Core WPF'ten bağımsız kalır.
- Kullanılanlar: `FluentWindow`, `TitleBar`, tema yöneticisi (`ApplicationThemeManager`, `SystemThemeWatcher`), vurgu rengi (`ApplicationAccentColorManager`), `SymbolIcon`, `Button` (Appearance), `InfoBar`, `ProgressRing`, kart ve metin stilleri.
- .NET 10'un yerleşik `ThemeMode` özelliği bu sürümde "yalnızca değerlendirme amaçlı" (WPF0001) olduğu için kullanılmaz.

### Core'a eklenenler (her biri testli)

1. **Tema ayarı:** `AppTheme` (`System`, `Light`, `Dark`); `AppSettings`'e `Theme` alanı (yoksa `System`, bozuk değerse `System`). Eski `settings.json` dosyaları sorunsuz okunur.
2. **Tema uygulayıcı:** Core'da küçük bir `IThemeService` arayüzü (`Apply(AppTheme)`), App'te WPF-UI ile gerçekleştirilir. `SettingsViewModel.Theme` değişince kaydeder ve uygular (dil ayarındaki desenle aynı).
3. **Güncelleme sayısı:** winget sağlam bulununca güncellemeler arka planda bir kez yüklenir; sayı menü rozetine bağlanır, her toplu iş bitince yenilenir. Hata olursa rozet gizlenir, hata kullanıcıyı rahatsız etmez (günlüğe yazılır).
4. **İşlemler paneli durumu:** etkin iş sayısı (sürüyor + sırada), panelin açık/kapalı durumu, yeni iş eklenince otomatik açılma, **Bitenleri temizle** komutu. Kuyruktan bitmiş öğeleri kaldırma iş parçacığı açısından güvenli olur ve sürmekte olan işi asla kaldırmaz.
5. **Harf avatarı hesaplayıcı:** ad ve kimlikten harf (InvariantCulture büyük harf; harf/rakam yoksa "?") ve renk sırası (kimliğin büyük harfli hâlinden kararlı bir özet, `% 8`). Aynı girdi her zaman aynı sonucu verir; tr-TR kültüründe de.
6. **Sürüm:** uygulama sürümü 0.1.0; Hakkında bunu derlemeden okur.

### App tarafı

- `MainWindow`, beş sayfa, `OperationListView` ve `ErrorBanner` yeniden yazılır. Bütün renk ve stiller tek bir kaynak sözlükte (`Themes/` altında) toplanır; sayfalar sabit renk yazmaz.
- Yeni metin anahtarları iki dile birden eklenir (kısa menü adları, tema adları, panel metinleri, Hakkında).
- Logo XAML vektör çizimi olarak eklenir; `.ico` dosyası (16, 20, 24, 32, 40, 48, 64, 256 px; küçük boylarda rozetsiz) bu çizimden üretilip `ApplicationIcon` olarak kullanılır.
- Bugünkü sayfa ViewModel'leri ve bağlamaları korunur; yalnız görünüm değişir.

## Erişilebilirlik

- Klavye ile bütün düğmelere ulaşılır, odak çerçevesi görünür.
- Yalnız ikonlu düğmelerde `AutomationProperties.Name` yerelleştirilmiş metindir.
- Karşıtlık oranları yukarıdaki tabloya uyar.

## Test ve doğrulama

- Mevcut otomatik testler değişmeden geçer; Core eklerinin her biri için yeni testler yazılır (önce test).
- `dotnet build` 0 uyarı, 0 hata.
- Her uygulama adımından sonra programın açık ve koyu temada ekran görüntüsü alınır ve proje sahibine gösterilir.
- Yayın paketi (`KolayKur.exe`) tek dosya olarak açılır; WPF-UI kaynakları tek dosya içinde çalışır; boyut artışı ölçülür.
- Sonda proje sahibi kısa bir elle deneme yapar (tema değişimi, iki dil, bir kurma).

## Riskler

- **WPF-UI API adları:** ilk adımda küçük bir derleme denemesiyle doğrulanır; uyuşmayan ad varsa belgeye işlenir.
- **Tek dosya yayın:** WPF-UI'ın kaynak sözlükleri tek dosyalık yayında yüklenmelidir; ilk adımda yayın denemesiyle doğrulanır.
- **Mica:** Windows 10'da yoktur; düz zemine düşer (Windows 10 zaten denenmedi).
- **Boyut:** `.exe` birkaç MB büyür; ölçülüp README'ye yazılır.

## Uygulama sırası (özet)

1. WPF-UI kurulumu, tema altyapısı (Core tema ayarı ve uygulayıcı), `FluentWindow`, başlık çubuğu ve sol menü. Tek dosya yayın denemesi. Ekran görüntüsü.
2. Ortak bileşenler: renk/stil sözlüğü, uygulama kartı, harf avatarı, bilgi çubuğu, İşlemler paneli (otomatik açılma, rozet, Bitenleri temizle).
3. Ara ve Güncellemeler sayfaları, güncelleme sayısı rozeti.
4. Yüklü uygulamalar, Ayarlar (tema seçimi, Hakkında, sürüm) ve Onarım sayfası.
5. Logo ve `.ico`, yayın paketi, README ve son kontrol.
