# KolayKur Yeni Tasarım Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** KolayKur'u, onaylanan B yönüne (Windows 11 tarzı, camgöbeği marka rengi, logo 2) göre yeniden görselleştirmek; işlevler aynı kalır.

**Architecture:** WPF-UI 4.3.0 yalnız `KolayKur.App`'e eklenir; pencere `FluentWindow` olur. Görünümle ilgili küçük durumlar (tema ayarı, işlem paneli, sayaçlar, harf avatarı) WPF'ten bağımsız `KolayKur.Core`'da, testli olarak eklenir. Gezinme bugünkü `ShellViewModel.CurrentPage` ile kalır.

**Tech Stack:** C#, .NET 10, WPF, WPF-UI 4.3.0, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-kolaykur-yeni-tasarim-design.md` (görsel taslaklar: claude.ai'da özel "KolayKur Tasarım Taslakları" sayfası, B satırı).

## Global Constraints

- WPF-UI paketi: `WPF-UI` 4.3.0, yalnız `src/KolayKur.App`'te. `KolayKur.Core` hiçbir WPF/WPF-UI tipine başvurmaz.
- .NET 10 `ThemeMode` (WPF0001) kullanılmaz.
- Marka vurgusu: açık `#0E7C7B`, koyu `#3DD6C6`; vurgu üstü yazı açık `#FFFFFF`, koyu `#000000`. Windows vurgu rengi izlenmez.
- Renk belirteçleri (açık / koyu): pencere zemini `#F3F3F3` / `#202020`, içerik katmanı `#F9F9F9` / `#272727`, kart `#FFFFFF` / `#2D2D2D`, kart çizgisi `#E5E5E5` / `#393939`, yazı `#1A1A1A` / `#FFFFFF`, ikincil yazı `#5C5C5C` / `#C8C8C8`, başarı `#0F7B0F` / `#6CCB5F`, uyarı `#9D5D00` / `#FCE100`.
- Sayfalar renk yazmaz; renkler `Themes/` sözlüklerinden `DynamicResource` ile gelir (tema anında değişebilsin).
- Tema seçenekleri `System` (varsayılan), `Light`, `Dark`; `settings.json`'a yazılır; pencere görünmeden önce uygulanır.
- Kullanıcıya görünen her metin `Strings.resx` (Türkçe) ve `Strings.en.resx` (İngilizce) dosyalarındadır; iki dosyanın anahtarları aynıdır (`Every_key_has_english_text` testi yeşil kalır).
- Gezinme `ShellViewModel.CurrentPage` ile yapılır; WPF-UI `NavigationView` ve sayfa gezinmesi kullanılmaz.
- Pencere varsayılanı 1100×720, en küçük 760×520. Sol menü 80 px, İşlemler paneli 300 px.
- Yazı tipi Segoe UI Variable; ikonlar WPF-UI `SymbolRegular`.
- Yalnız ikonlu her düğmenin `AutomationProperties.Name`'i yerelleştirilmiş metindir.
- Mevcut testler değişmeden geçer (bir görev açıkça uyarlamadıkça). `dotnet build KolayKur.slnx` 0 uyarı, 0 hata.
- Program tek `KolayKur.exe` olarak yayınlanır.
- **Bu makinede hiçbir uygulama kurulmaz, güncellenmez, kaldırılmaz.** Ekran görüntüsü betiği yalnız arama ve listeleme yapar; Kur, Güncelle, Kaldır, Listeden kur, Onar düğmelerine asla basılmaz.

## Review Focus

1. **Çalışırken tema değişimi:** Sistem → Koyu → Açık geçişinde açık olan bütün sayfalar, kartlar, panel ve rozetler hemen yeni renge geçmeli; `StaticResource` ile bağlanmış bir renk eski temada takılı kalır. (Görev 2 ve 6'daki ekran görüntüleri ile kod incelemesi; Görev 6'da çalışırken tema değiştirme adımı.)
2. **Uzun adlar:** "Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247" gibi adlar ve uzun yayıncılar kartı taşırmamalı, `…` ile kısalmalı; İngilizce/Türkçe menü yazıları 80 px menüye sığmalı. (Görev 4 kart şablonu `TextTrimming`, Görev 6 Yüklü sayfası ekran görüntüsü gerçek uzun adlarla.)
3. **Dar pencere:** 760×520'de sol menü, açık İşlemler paneli ve içerik kesilmeden görünmeli; kart ızgarası tek sütuna düşmeli. (Görev 3'te 760×520 ekran görüntüsü.)
4. **Kalabalık İşlemler paneli:** "Listeden kur" 500'e kadar iş ekleyebilir; panel kaydırılabilir olmalı, sayaç doğru kalmalı, "Bitenleri temizle" yalnız bitenleri silmeli. (Görev 3 Core testleri: çok öğeli sayaç ve temizleme.)
5. **Dil değişimi:** İngilizceye geçince menü, panel durumları, sayı metinleri ve tema adları anında değişmeli. (Görev 4/5/6 "follows language" testleri ve Görev 6 İngilizce ekran görüntüsü.)

---

## Dosya yapısı

```text
src/KolayKur.Core/Support/AppTheme.cs            AppTheme enum
src/KolayKur.Core/Support/IThemeService.cs       Tema uygulama arayüzü
src/KolayKur.Core/Support/AppSettings.cs         + Theme (hoşgörülü okuma)
src/KolayKur.Core/Support/AppPaths.cs            + KOLAYKUR_DATA_DIR desteği
src/KolayKur.Core/Support/PackageAvatar.cs       Harf avatarı hesaplayıcı
src/KolayKur.Core/Queue/OperationQueue.cs        + ClearFinished()
src/KolayKur.Core/ViewModels/*.cs                küçük ekler (aşağıda)
src/KolayKur.App/Themes/Brand.Light.xaml         Açık tema belirteçleri ve avatar paleti
src/KolayKur.App/Themes/Brand.Dark.xaml          Koyu tema belirteçleri ve avatar paleti
src/KolayKur.App/Themes/Styles.xaml              Kart, başlık, çip, menü düğmesi stilleri
src/KolayKur.App/Themes/Logo.xaml                LogoImage, LogoSmallImage (DrawingImage)
src/KolayKur.App/WpfUiThemeService.cs            IThemeService'in WPF-UI gerçekleştirmesi
src/KolayKur.App/Converters/EnumEqualsConverter.cs
src/KolayKur.App/Views/Layout/CardGridPanel.cs   Akan kart ızgarası paneli
src/KolayKur.App/Views/PackageAvatarView.xaml    40 px harf avatarı
src/KolayKur.App/Views/ErrorInfoBar.xaml         ErrorBanner'ın yerine (InfoBar)
src/KolayKur.App/Views/OperationsPane.xaml       OperationListView'in yerine (sağ panel)
src/KolayKur.App/Assets/KolayKur.ico             Uygulama ikonu
tools/IconGen/                                    Logo.xaml'dan .ico üreten küçük araç
tools/screenshot.ps1                              Ekran görüntüsü betiği (salt okur)
```

---

### Task 1: Tema ayarı (Core)

**Files:**
- Create: `src/KolayKur.Core/Support/AppTheme.cs`, `src/KolayKur.Core/Support/IThemeService.cs`
- Modify: `src/KolayKur.Core/Support/AppSettings.cs`, `src/KolayKur.Core/ViewModels/SettingsViewModel.cs`
- Create: `tests/KolayKur.Core.Tests/Fakes/FakeThemeService.cs`
- Test: `tests/KolayKur.Core.Tests/Support/AppSettingsTests.cs`, `tests/KolayKur.Core.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: `public enum AppTheme { System, Light, Dark }`; `public interface IThemeService { void Apply(AppTheme theme); }`; `AppSettings` record'una son parametre `AppTheme Theme = AppTheme.System`; `SettingsViewModel(ShellViewModel shell, FileLog log, IDialogService dialogs, string settingsPath, IThemeService? themes = null)`, `public AppTheme Theme { get; set; }`, `public string AppVersion { get; init; } = string.Empty;`

- [ ] **Step 1: AppSettings testlerini yaz** (mevcut geçici klasör desenini kullan)

```csharp
[Fact] public void Theme_defaults_to_system_when_missing()
{ // dosya: {"Language":"en","LicenseNoticeShown":true}
  var s = AppSettings.Load(path);
  Assert.Equal(AppTheme.System, s.Theme); Assert.Equal("en", s.Language); Assert.True(s.LicenseNoticeShown); }

[Fact] public void Unknown_theme_falls_back_to_system_and_keeps_other_values()
{ // dosya: {"Language":"en","Theme":"Purple"}
  var s = AppSettings.Load(path);
  Assert.Equal(AppTheme.System, s.Theme); Assert.Equal("en", s.Language); }

[Fact] public void Theme_round_trips_as_text()
{ new AppSettings(Theme: AppTheme.Dark).Save(path);
  Assert.Contains("\"Dark\"", File.ReadAllText(path));
  Assert.Equal(AppTheme.Dark, AppSettings.Load(path).Theme); }
```

- [ ] **Step 2: SettingsViewModel testlerini yaz** (`SettingsViewModelTests`'teki mevcut kurulum yardımcılarını kullan)

```csharp
[Fact] public void Theme_starts_from_saved_setting()          // settings.json Theme=Dark → vm.Theme == Dark
[Fact] public void Theme_change_is_applied_and_saved()         // vm.Theme = Dark → fake.Applied == [Dark]; dosyada Theme Dark; Language ve LicenseNoticeShown korunur
[Fact] public void Theme_save_failure_still_applies()          // ayar yolu bir klasör → atama fırlatmaz, fake.Applied == [Dark], günlükte hata
[Fact] public void Setting_the_same_theme_does_nothing()       // System iken System atanır → fake.Applied boş, dosya yazılmaz
```

- [ ] **Step 3: Çalıştır, başarısız olduğunu gör**

Run: `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~AppSettings|FullyQualifiedName~SettingsViewModel"`
Expected: FAIL (tipler/özellikler yok)

- [ ] **Step 4: Uygula**

`Theme` JSON'da metin olarak yazılır; bilinmeyen değer `System`'e düşer ve **diğer alanları bozmaz** (yalnız bu özellik için hoşgörülü bir `JsonConverter<AppTheme>`). `SettingsViewModel.Theme` kurucuda `AppSettings.Load(settingsPath).Theme` ile başlar; atama değer değişmişse `themes?.Apply(value)` çağırır ve `SaveLanguage` desenindeki gibi `AppSettings.Load(path) with { Theme = value }` kaydeder (kayıt hatası günlüğe yazılır, uygulama geri alınmaz).

- [ ] **Step 5: Çalıştır, geçtiğini gör; bütün Real dışı testleri bir kez çalıştır**

Run: aynı komut, sonra `dotnet test --filter "Category!=Real"` → PASS, uyarı yok

- [ ] **Step 6: Commit** — `feat: tema ayari (Sistem/Acik/Koyu) ve tema arayuzu`

---

### Task 2: WPF-UI temeli, pencere kabuğu ve ekran görüntüsü betiği

**Files:**
- Modify: `src/KolayKur.App/KolayKur.App.csproj` (WPF-UI 4.3.0), `src/KolayKur.App/App.xaml`, `src/KolayKur.App/App.xaml.cs`, `src/KolayKur.App/MainWindow.xaml`, `src/KolayKur.App/MainWindow.xaml.cs`
- Create: `src/KolayKur.App/Themes/Brand.Light.xaml`, `Brand.Dark.xaml`, `Styles.xaml`, `Logo.xaml`, `src/KolayKur.App/WpfUiThemeService.cs`, `src/KolayKur.App/Converters/EnumEqualsConverter.cs`, `tools/screenshot.ps1`
- Modify: `src/KolayKur.Core/Support/AppPaths.cs`; Strings.resx ve Strings.en.resx
- Test: `tests/KolayKur.Core.Tests/Support/AppPathsTests.cs`

**Interfaces:**
- Consumes: Task 1 `AppTheme`, `IThemeService`, `SettingsViewModel(..., IThemeService? themes)`.
- Produces: kaynak anahtarları (iki tema sözlüğünde de aynı): `BrandAccentBrush`, `BrandAccentForegroundBrush`, `WindowBackgroundBrush`, `PageBackgroundBrush`, `CardBrush`, `CardStrokeBrush`, `TextPrimaryBrush`, `TextSecondaryBrush`, `SuccessBrush`, `SuccessBackgroundBrush`, `CautionBrush`, `AvatarBrush0` … `AvatarBrush7`; `Styles.xaml`: `PageTitleTextStyle`, `SecondaryTextStyle`, `AppCardStyle` (Border), `VersionChipStyle`, `RailButtonStyle` (RadioButton); `Logo.xaml`: `LogoImage`, `LogoSmallImage`; `WpfUiThemeService : IThemeService` with `void Attach(Window window)`; `EnumEqualsConverter` (ConverterParameter = enum değeri; ConvertBack true → parametre, false → `Binding.DoNothing`); `tools/screenshot.ps1 -Theme -Lang -Page -Width -Height -Search -Out`.

- [ ] **Step 1: WPF-UI API doğrulaması.** Paketi ekle; şu adların 4.3.0'da derlendiğini küçük bir derlemeyle doğrula ve rapora yaz: `FluentWindow` (`ExtendsContentIntoTitleBar`, `WindowBackdropType`), `TitleBar` (sağ tarafa içerik koyma yolu), `ApplicationThemeManager.Apply/GetSystemTheme/Changed`, `SystemThemeWatcher.Watch/UnWatch`, `ApplicationAccentColorManager.Apply`, `SymbolIcon`, `InfoBar`, `ProgressRing`, `Button.Appearance`. Uymayan ad varsa en yakın 4.3.0 karşılığını kullan ve raporda belirt.
- [ ] **Step 2: AppPaths testlerini yaz** (ortam değişkeni testleri paralel çalışmayan bir koleksiyonda; `finally`'de eski değer geri yüklenir)

```csharp
[Fact] public void Root_uses_data_dir_override_when_set()   // KOLAYKUR_DATA_DIR=<geçici mutlak yol> → Root == o yol; Logs, Downloads, SettingsFile onun altında
[Fact] public void Root_ignores_relative_override()          // KOLAYKUR_DATA_DIR="goreli" → Root == %LocalAppData%\KolayKur
```

- [ ] **Step 3: Çalıştır → FAIL; `AppPaths`'i her erişimde değişkeni okuyacak şekilde değiştir; çalıştır → PASS.**

Run: `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~AppPaths"`

- [ ] **Step 4: Tema sözlükleri, stiller ve logo.** Renkler Global Constraints'teki değerlerdir. Avatar paleti (açık/koyu aynı, beyaz harfle ≥ 4.5:1): `#1F5FA8`, `#B33A1A`, `#4752C4`, `#0E7C7B`, `#B42318`, `#6F42C1`, `#3C873A`, `#5B5B66`. `LogoImage` spec'teki logo geometrisidir; `LogoSmallImage` rozetsizdir (yalnız zemin ve K).
- [ ] **Step 5: `WpfUiThemeService`.** `Apply(Light|Dark)`: sistem izlemeyi bırakır, WPF-UI temasını uygular. `Apply(System)`: sistem temasını uygular ve pencere bağlıysa `SystemThemeWatcher` ile izler. Hangi yoldan değişirse değişsin (elle ya da Windows), `ApplicationThemeManager.Changed` üzerinde marka sözlüğünü (`Brand.Light`/`Brand.Dark`) değiştirir ve vurgu rengini marka rengine ayarlar. `Attach(window)` pencere oluşunca çağrılır ve Mica'yı etkinleştirir.
- [ ] **Step 6: Pencere kabuğu.** `MainWindow` → `ui:FluentWindow` (Mica, başlık pencereye gömülü, 1100×720, en küçük 760×520). Başlık: `LogoImage` (18 px) ve `App_Title`. Sol menü 80 px: dört `RadioButton` (`RailButtonStyle`; ikon + kısa yazı), `IsChecked` ↔ `CurrentPage` (`EnumEqualsConverter`), `IsEnabled` ↔ `IsMenuEnabled`; Ara/Güncelle/Yüklü üstte, Ayarlar en altta; seçili öğede dolgu ve solda 3 px `BrandAccentBrush` çizgi. İkonlar: `Search24`, `ArrowSync24`, `Apps24`, `Settings24`. Sayfa alanı `PageBackgroundBrush`, sol üst köşe 8 px yuvarlak. Denetim sürerken ortada `ProgressRing` ve `Startup_Checking`. Bugünkü alt işlem listesi Görev 3'e kadar yerinde kalır.
  Yeni metinler: `Nav_Search` Ara / Search, `Nav_Updates` Güncelle / Updates, `Nav_Installed` Yüklü / Installed, `Nav_Settings` Ayarlar / Settings.
- [ ] **Step 7: Kurulum sırası (`App.xaml.cs`).** `WpfUiThemeService` oluştur; kayıtlı temayı **pencere oluşmadan önce** uygula; pencere oluşunca `Attach`; `SettingsViewModel`'e `themes` olarak ver.
- [ ] **Step 8: `tools/screenshot.ps1`.** Geçici bir veri klasörü oluşturur, içine `settings.json` yazar (`Language`, `LicenseNoticeShown: true`, `Theme`), `KOLAYKUR_DATA_DIR` ile programı başlatır, ana pencereyi bekler (en çok 30 sn) ve menü etkinleşene kadar bekler. Pencereyi istenen boyuta getirir. `-Page` için menü düğmesine UI Automation ile basar. `-Search` verilirse arama kutusuna yazıp "Ara"ya basar. Pencereyi PNG olarak `-Out` yoluna kaydeder, süreci kapatır, geçici klasörü siler. Başka hiçbir düğmeye basmaz.
- [ ] **Step 9: Doğrula.** `dotnet build KolayKur.slnx` 0 uyarı; `dotnet test --filter "Category!=Real"` PASS. Ekran görüntüleri: `out/screenshots/t2-search-light.png`, `t2-search-dark.png`. Tek dosya yayını (`README`'deki komut) ve `out/release/KolayKur.exe` ile bir ekran görüntüsü daha (`t2-release-light.png`); WPF-UI kaynakları tek dosyada yüklenmeli. Exe boyutunu rapora yaz.
- [ ] **Step 10: Commit** — `feat: WPF-UI pencere kabugu, tema servisi ve sol menu`

---

### Task 3: İşlemler paneli

**Files:**
- Modify: `src/KolayKur.Core/Queue/OperationQueue.cs`, `src/KolayKur.Core/ViewModels/OperationListViewModel.cs`, `src/KolayKur.App/MainWindow.xaml(.cs)`, resx dosyaları
- Create: `src/KolayKur.App/Views/OperationsPane.xaml(.cs)`; Delete: `src/KolayKur.App/Views/OperationListView.xaml(.cs)`
- Test: `tests/KolayKur.Core.Tests/Queue/OperationQueueTests.cs`, `tests/KolayKur.Core.Tests/ViewModels/OperationListViewModelTests.cs`

**Interfaces:**
- Produces: `OperationQueue.ClearFinished() : int` (kuyruğun bağlamında çağrılır; `Succeeded`, `Failed`, `Cancelled`, `NotFound` öğeleri `Items`'tan kaldırır; `Queued`/`Running` asla kaldırılmaz; kaldırılan sayıyı döner). `OperationListViewModel`: `int ActiveCount` (Queued + Running), `bool HasActive`, `bool HasFinished`, `bool IsPaneOpen` (gözlenebilir; `Items`'a öğe eklenince `true`), `IRelayCommand TogglePaneCommand`, `IRelayCommand ClosePaneCommand`, `IRelayCommand ClearFinishedCommand` (`HasFinished` iken etkin).

- [ ] **Step 1: Testleri yaz** (`FakeWingetClient` `Hold`/`SetOutcome` ve mevcut kuyruk test yardımcılarıyla)

```csharp
[Fact] public void ClearFinished_removes_only_finished_items()       // A bitti, B tutuluyor (Running), C sırada → ClearFinished()==1; Items == [B, C]
[Fact] public void ClearFinished_on_empty_queue_returns_zero()
[Fact] public void Finished_package_can_be_enqueued_again_after_clear()
[Fact] public void Active_count_counts_queued_and_running()          // 3 iş, ilki tutuluyor → 3; hepsi bitince 0, HasActive false
[Fact] public void Active_count_with_many_items()                     // 200 iş eklenir, ilki tutulur → ActiveCount 200; bırakılıp boşalınca 0
[Fact] public void Pane_opens_when_an_item_is_added()                 // başta false; Enqueue → true; ClosePane → false; yeni Enqueue → true
[Fact] public void Clear_finished_command_needs_a_finished_item()     // bitmiş öğe yokken CanExecute false; biri bitince true
```

- [ ] **Step 2: Çalıştır → FAIL** — `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~Queue|FullyQualifiedName~OperationListViewModel"`
- [ ] **Step 3: Uygula.** Sayaçlar her öğenin `Status` değişimiyle ve koleksiyon değişimiyle yeniden hesaplanır (kaldırılan öğenin aboneliği bırakılır).
- [ ] **Step 4: Çalıştır → PASS** (aynı komut, ardından Real dışı bütün testler; kuyruk testlerini 10 kez döngüde çalıştır, kararsızlık yok)
- [ ] **Step 5: Panel görünümü.** `OperationsPane`: başlık (`OperationList_Title`), "Bitenleri temizle" (`Ops_ClearFinished` Bitenleri temizle / Clear finished), kapat düğmesi (`Ops_ClosePane` İşlemler panelini kapat / Close operations panel; yalnız ikon, `AutomationProperties.Name` bu metin); kaydırılabilir, sanallaştırılmış liste; her öğe bir kart: `Package.Name`, yerelleştirilmiş durum (`OperationTexts.StatusKey`), `DetailText`, `Running` iken ilerleme çubuğu (`Percent` yoksa belirsiz), ipucunda `TechnicalDetail`; boşken `OperationList_Empty`; altta `SummaryText`. `MainWindow`: sağda 300 px sütun `IsPaneOpen` ile görünür; başlıkta "İşlemler" geçiş düğmesi (`IsChecked` ↔ `IsPaneOpen`, `HasActive` iken `ActiveCount` rozeti). Eski alt liste kaldırılır.
- [ ] **Step 6: Doğrula.** Build 0 uyarı; ekran görüntüleri: panel açıkken (betik başlıktaki "İşlemler" düğmesine basar) `t3-pane-light.png`, `t3-pane-dark.png`, ve 760×520 boyutunda `t3-narrow-light.png`.
- [ ] **Step 7: Commit** — `feat: sagda acilip kapanan islemler paneli ve bitenleri temizle`

---

### Task 4: Ortak bileşenler ve Ara sayfası

**Files:**
- Create: `src/KolayKur.Core/Support/PackageAvatar.cs`, `src/KolayKur.App/Views/Layout/CardGridPanel.cs`, `src/KolayKur.App/Views/PackageAvatarView.xaml(.cs)`, `src/KolayKur.App/Views/ErrorInfoBar.xaml(.cs)`
- Modify: `src/KolayKur.Core/ViewModels/SearchViewModel.cs`, `src/KolayKur.App/Views/SearchPage.xaml(.cs)`, resx dosyaları; `src/KolayKur.App/Views/UpdatesPage.xaml` ve `InstalledPage.xaml` yalnızca `ErrorBanner` → `ErrorInfoBar` (`ShowRetry="True"`, bugünkü davranış) değişikliğiyle; Delete: `src/KolayKur.App/Views/ErrorBanner.xaml(.cs)`
- Test: `tests/KolayKur.Core.Tests/Support/PackageAvatarTests.cs`, `tests/KolayKur.Core.Tests/ViewModels/SearchViewModelTests.cs`

**Interfaces:**
- Produces: `public sealed record AvatarInfo(string Letter, int ColorIndex);` `public static class PackageAvatar { public const int PaletteSize = 8; public static AvatarInfo For(string id, string name); }`; `SearchViewModel`: `int SelectedCount`, `string? ResultCountText` (henüz arama yoksa `null`); `InstallSelectedCommand` yalnız `SelectedCount > 0` iken etkin. `PackageAvatarView` (bağımlılık özellikleri `PackageId`, `PackageName`; `AvatarBrush{ColorIndex}` kaynağını `SetResourceReference` ile alır). `ErrorInfoBar` (`ErrorKey`, `ShowRetry`, `RetryCommand`). `CardGridPanel` (`MinColumnWidth` varsayılan 220, `Spacing` varsayılan 12; eşit genişlikte sütunlar, sütun sayısı = max(1, ⌊(genişlik + aralık) / (MinColumnWidth + aralık)⌋)).

- [ ] **Step 1: Testleri yaz**

```csharp
[Theory]
[InlineData("7zip.7zip", "7-Zip", "7")]
[InlineData("VideoLAN.VLC", "  vlc media", "V")]
[InlineData("x", "çay", "Ç")]
[InlineData("x", "ılık", "I")]
[InlineData("x", "--", "?")]
[InlineData("x", "", "?")]
public void Letter_is_first_letter_or_digit_uppercased(string id, string name, string letter)
    => Assert.Equal(letter, PackageAvatar.For(id, name).Letter);

[Theory]   // FNV-1a 32 bit; kimliğin ToUpperInvariant() hâlinin her UTF-16 karakteri tek değer olarak; % 8
[InlineData("Git.Git", 5)]
[InlineData("7zip.7zip", 1)]
[InlineData("VideoLAN.VLC", 6)]
public void Color_index_is_pinned(string id, int index) => Assert.Equal(index, PackageAvatar.For(id, "a").ColorIndex);

[Fact] public void Color_index_ignores_case_and_turkish_culture()  // tr-TR altında For("INTEL.Driver") == For("intel.driver") == 1; kültür finally'de geri yüklenir

[Fact] public void Selected_count_follows_checkboxes()              // 3 sonuçtan 2'si seçilir → 2; InstallSelectedCommand.CanExecute true; seçim kalkınca 0 ve false
[Fact] public void Result_count_text_shows_query_and_count()        // "git" araması 2 sonuç → string.Format(Loc["Search_ResultCount"], "git", 2)
[Fact] public void Result_count_text_follows_language()             // SetLanguage("en") → İngilizce metin; dil finally'de "tr"
```

- [ ] **Step 2: Çalıştır → FAIL** — `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~PackageAvatar|FullyQualifiedName~SearchViewModel"`
- [ ] **Step 3: Uygula** (renk sırası `string.GetHashCode()` ile **hesaplanmaz**; süreçten sürece değişir). Harf: adın kırpılmış hâlinde ilk harf ya da rakam, `ToUpperInvariant`; yoksa `"?"`.
- [ ] **Step 4: Çalıştır → PASS** (aynı komut, sonra Real dışı bütün testler)
- [ ] **Step 5: Bileşenler ve sayfa.** `ErrorInfoBar`: WPF-UI `InfoBar` (hata), başlık `Loc[ErrorKey]`, kapatılamaz, `ErrorKey` doluyken açık, `ShowRetry` iken `Error_Retry` düğmesi. Ara sayfası: başlık (`PageTitleTextStyle`, `Menu_Search`); arama kutusu (`Search24` ikonu, `Search_Prompt` ipucu, Enter → `SearchCommand`) ve birincil `Search_Action`; `ResultCountText`; ikincil "Seçilenleri kur (N)" (`Search_InstallSelectedCount` "Seçilenleri kur ({0})" / "Install selected ({0})"); `CardGridPanel` içinde kartlar: `PackageAvatarView`, ad, yayıncı, kimlik (küçük), sağ üstte seçim kutusu, sürüm çipi (`AvailableVersion ?? InstalledVersion`), `InstalledVersion` doluysa yeşil "Kurulu" çipi (`Search_Installed` Kurulu / Installed), değilse birincil "Kur"; seçili kartın kenarı 2 px `BrandAccentBrush`. Bütün metin alanlarında `TextTrimming="CharacterEllipsis"`. Yükleniyor ve boş durumları. `ErrorInfoBar ShowRetry="True"`.
  Yeni metin: `Search_ResultCount` "“{0}” için {1} sonuç" / "{1} results for “{0}”".
- [ ] **Step 6: Doğrula.** Build 0 uyarı; ekran görüntüleri `-Search git`: `t4-search-light.png`, `t4-search-dark.png`, ve 760×520 `t4-search-narrow.png`.
- [ ] **Step 7: Commit** — `feat: kart bilesenleri, harf avatari ve yeni ara sayfasi`

---

### Task 5: Güncellemeler sayfası ve menü rozeti

**Files:**
- Modify: `src/KolayKur.Core/ViewModels/ShellViewModel.cs`, `src/KolayKur.Core/ViewModels/UpdatesViewModel.cs`, `src/KolayKur.App/App.xaml.cs`, `src/KolayKur.App/MainWindow.xaml(.cs)`, `src/KolayKur.App/Views/UpdatesPage.xaml(.cs)`, resx dosyaları
- Test: `tests/KolayKur.Core.Tests/ViewModels/ShellViewModelTests.cs`, `tests/KolayKur.Core.Tests/ViewModels/UpdatesViewModelTests.cs`

**Interfaces:**
- Consumes: Task 4 `ErrorInfoBar`, `PackageAvatarView`, `CardGridPanel`, stiller.
- Produces: `ShellViewModel.BecameHealthy` (`event EventHandler?`; `Diagnosis` önceki değeri `null` ya da sağlıksızken sağlıklı bir değere geçtiğinde, çağıranın bağlamında bir kez). `UpdatesViewModel`: `int UpdateCount`, `bool HasUpdates`, `string? CountText` (0 iken `null`; `Updates_Count` "{0} güncelleme var" / "{0} updates available"; dil değişince yenilenir).

- [ ] **Step 1: Testleri yaz**

```csharp
[Fact] public async Task Became_healthy_raised_once_on_healthy_start()
[Fact] public async Task Became_healthy_not_raised_when_start_is_broken()
[Fact] public async Task Became_healthy_raised_after_successful_repair()    // bozuk başlangıç → onarım sağlıklı döner → 1 kez
[Fact] public async Task Healthy_recheck_does_not_raise_again()              // sağlıklı → RecheckAsync sağlıklı → toplam 1
[Fact] public async Task Update_count_follows_loaded_list()                  // sahte 3 güncelleme → UpdateCount 3, HasUpdates true, CountText dolu
[Fact] public async Task Failed_load_hides_badge()                           // CatalogUnavailableException → UpdateCount 0, HasUpdates false, CountText null
[Fact] public async Task Count_text_follows_language()                       // en → İngilizce; finally "tr"
```

- [ ] **Step 2: Çalıştır → FAIL** — `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~ShellViewModel|FullyQualifiedName~UpdatesViewModel"`
- [ ] **Step 3: Uygula; Step 4: Çalıştır → PASS** (ardından Real dışı bütün testler)
- [ ] **Step 5: Bağla ve sayfayı yap.** `App.xaml.cs`: `shell.BecameHealthy += (_, _) => _ = updates.LoadCommand.ExecuteAsync(null);` (pencere oluşturulduktan sonra, `StartAsync`'ten önce bağlanır). Menüdeki "Güncelle" öğesinde `HasUpdates` iken `UpdateCount` rozeti (`BrandAccentBrush` zemin). Güncellemeler sayfası: başlık, `CountText`, birincil `Updates_UpdateAll` (`HasUpdates` yoksa devre dışı); kartlar: avatar, ad, yayıncı, "`InstalledVersion` → `AvailableVersion`" (ikincil çip, `ArrowRight16`, vurgu renkli çip), ikincil `Updates_Update`; yükleniyor, boş (`Updates_Empty`) ve `ErrorInfoBar ShowRetry="True"`.
- [ ] **Step 6: Doğrula.** Build 0 uyarı; `t5-updates-light.png`, `t5-updates-dark.png` (gerçek liste; yalnız okunur).
- [ ] **Step 7: Commit** — `feat: guncellemeler sayfasi ve menude guncelleme rozeti`

---

### Task 6: Yüklü uygulamalar, Ayarlar ve Onarım sayfaları

**Files:**
- Modify: `src/KolayKur.Core/ViewModels/InstalledViewModel.cs`, `src/KolayKur.App/Views/InstalledPage.xaml(.cs)`, `SettingsPage.xaml(.cs)`, `RepairPage.xaml(.cs)`, resx dosyaları
- Test: `tests/KolayKur.Core.Tests/ViewModels/InstalledViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 `SettingsViewModel.Theme`, `AppVersion`; Task 2 `EnumEqualsConverter`, `LogoImage`; Task 4 bileşenleri.
- Produces: `InstalledViewModel.CanRetry` (`ErrorKey` `Error_NoInternet` ya da `Error_Generic` iken `true`; liste dosyası hatalarında `false`; `ErrorKey` değişince bildirilir), `InstalledViewModel.CountText` (`Installed_Count` "{0} uygulama" / "{0} apps"; 0 iken `null`; dil değişince yenilenir).

- [ ] **Step 1: Testleri yaz**

```csharp
[Fact] public async Task List_file_error_cannot_retry()        // bozuk liste dosyası → ErrorKey Error_ListFile, CanRetry false
[Fact] public async Task Load_failure_can_retry()              // CatalogUnavailableException → CanRetry true
[Fact] public async Task Count_text_shows_package_count()      // 3 paket → string.Format(Loc["Installed_Count"], 3)
```

- [ ] **Step 2: Çalıştır → FAIL; Step 3: Uygula; Step 4: Çalıştır → PASS** — `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~InstalledViewModel"`, ardından Real dışı bütün testler
- [ ] **Step 5: Sayfalar.** Yüklü: başlık, `CountText`, ikincil `Installed_Export`, birincil `Installed_Import`; kartlar (avatar, ad, yayıncı, `InstalledVersion` çipi, ikincil `Installed_Uninstall` + `Delete16`); `ErrorInfoBar ShowRetry="{Binding CanRetry}"`. Ayarlar: satır kartları — Tema (üç `RadioButton`, bölümlü görünüm, `Theme` ↔ `EnumEqualsConverter`; `Theme_Title` Tema / Theme, `Theme_Description` "Uygulamanın açık ya da koyu görünmesi. "Sistem", Windows ayarını izler." / "Light or dark look. "System" follows Windows.", `Theme_System` Sistem / System, `Theme_Light` Açık / Light, `Theme_Dark` Koyu / Dark), Dil (mevcut seçim; `Settings_LanguageDescription` "Arayüz dili. Değişiklik hemen uygulanır." / "Interface language. Changes apply immediately."), Winget durumu (`StatusKey` metni + `WingetVersion`, ikincil `Settings_Repair`), Günlük (`Settings_LogDescription` "Bir sorun olursa ne olduğunu bu dosya gösterir." / "If something goes wrong, this file shows what happened.", ikincil `Settings_OpenLog`), Hakkında (`LogoImage` 28 px, `App_Title`, `About_Version` "Sürüm {0}" / "Version {0}"). Onarım: ortalanmış kart (en çok 560 px), mevcut metinler ve `StatusText`, çalışırken `ProgressRing`, birincil `Repair_Action`, `ErrorInfoBar ShowRetry="False"`.
- [ ] **Step 6: Doğrula.** Build 0 uyarı; `t6-installed-light.png`, `t6-installed-dark.png`, `t6-settings-light.png`, `t6-settings-en-dark.png` (`-Lang en`). Çalışırken tema değişimi: betik Ayarlar'da "Koyu" düğmesine UI Automation ile basar ve `t6-settings-switched-dark.png` alır (bu düğme yalnız ayar değiştirir, güvenlidir). Onarım sayfası bu makinede açılmaz (winget sağlam); raporda belirt.
- [ ] **Step 7: Commit** — `feat: yuklu uygulamalar, ayarlar ve onarim sayfalarinin yeni gorunumu`

---

### Task 7: Logo ikonu, sürüm ve yayın

**Files:**
- Create: `tools/IconGen/IconGen.csproj`, `tools/IconGen/Program.cs`, `src/KolayKur.App/Assets/KolayKur.ico`
- Modify: `KolayKur.slnx`, `src/KolayKur.App/KolayKur.App.csproj`, `src/KolayKur.App/MainWindow.xaml`, `src/KolayKur.App/App.xaml.cs`, `README.md`

**Interfaces:**
- Consumes: Task 2 `Themes/Logo.xaml` (`LogoImage`, `LogoSmallImage`), Task 1 `SettingsViewModel.AppVersion`.

- [ ] **Step 1: IconGen.** `dotnet run --project tools/IconGen -- src/KolayKur.App/Themes/Logo.xaml src/KolayKur.App/Assets/KolayKur.ico`: sözlüğü `XamlReader` ile yükler; 16, 20, 24 px için `LogoSmallImage`, 32, 40, 48, 64, 256 px için `LogoImage` çizer (`RenderTargetBitmap`, saydam zemin); PNG kareli `.ico` yazar. Logo geometrisi kopyalanmaz, tek kaynak `Logo.xaml`'dır.
- [ ] **Step 2: Doğrula** — `.ico` dizinindeki boyutları okuyan kısa bir PowerShell komutu `16,20,24,32,40,48,64,256` yazdırır.
- [ ] **Step 3: Bağla.** csproj: `<ApplicationIcon>Assets\KolayKur.ico</ApplicationIcon>`, `<Version>0.1.0</Version>`; pencere `Icon` aynı dosya (Resource). Kurulumda `AppVersion` = derlemenin sürümü, üç parça (`0.1.0`).
- [ ] **Step 4: Yayın ve son kontrol.** README'deki yayın komutu; `out/release/KolayKur.exe` boyutu; `dotnet build` 0 uyarı; `dotnet test --filter "Category!=Real"` PASS. Yayınlanan exe ile son ekran görüntüleri: dört sayfa × açık/koyu (`t7-*.png`) ve İngilizce Ayarlar.
- [ ] **Step 5: README.** "Neler yapar"a "Açık, koyu veya Windows'a uyan tema" satırı; boyut rakamlarını güncelle; "Denenenler" tablosuna "Yeni görünüm (otomatik testler ve ekran görüntüleri)" satırı. Denenmeyeni denenmiş gibi yazma.
- [ ] **Step 6: Commit** — `feat: uygulama ikonu, surum 0.1.0 ve yeni gorunumle yayin`

---

## Sonda proje sahibinin elle kontrolü (otomatik değil)

- Ayarlar'dan tema Sistem/Açık/Koyu; "Sistem"deyken Windows temasını değiştir, program hemen uymalı.
- Dil İngilizce ↔ Türkçe; bütün sayfalar ve İşlemler paneli.
- Küçük bir uygulama kur ve kaldır: panel kendiliğinden açılmalı, ilerleme ve durum görünmeli, "Bitenleri temizle" çalışmalı.
- Görev çubuğunda ve pencere başlığında yeni ikon.
