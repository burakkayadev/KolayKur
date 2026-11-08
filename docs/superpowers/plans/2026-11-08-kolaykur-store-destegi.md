# KolayKur Microsoft Store Desteği Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aramaya ve kurmaya Microsoft Store kaynağını eklemek; Netflix gibi Store uygulamaları "Kur" ile kurulabilsin.

**Architecture:** `ComWingetClient` winget ve msstore uzak kataloglarını yüklü katalogla birleştirir; her `PackageInfo` kaynağını taşır. Liste dosyası kaynaklara göre yazılır. Arayüz Store kartlarını etiketler. Lisans notu sürümlenir.

**Tech Stack:** C#, .NET 10, WPF, WPF-UI 4.3.0, Microsoft.WindowsPackageManager.ComInterop 1.29.380, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-08-kolaykur-store-destegi-design.md`

## Global Constraints

- Dal `store-destegi` (`yeni-tasarim` üzerine). Yeni görünüm planının "Global Constraints" bölümü aynen geçerli: `docs/superpowers/plans/2026-11-08-kolaykur-yeni-tasarim.md`.
- Kaynak adları tam olarak `"winget"` ve `"msstore"`; karşılaştırmalar `StringComparison.OrdinalIgnoreCase`.
- `PackageInfo` geriye uyumlu kalır (yalnız isteğe bağlı son parametre).
- Store'a ulaşılamaması aramayı ve listeleri bozmaz: winget ile devam, günlüğe kayıt, kullanıcıya hata yok.
- Kullanıcıya görünen her metin iki resx dosyasında; TR/EN eşliği testi yeşil.
- **Bu makinede hiçbir uygulama kurulmaz, güncellenmez, kaldırılmaz.** Gerçek testler yalnız arar ya da listeler. Yalnız okuyan komutlarda `--accept-source-agreements` kullanılabilir (proje sahibi Store kullanımını onayladı).
- `dotnet build KolayKur.slnx` 0 uyarı, 0 hata; `dotnet test --filter "Category!=Real"` geçer.

## Review Focus

1. **Store'a ulaşılamaz:** arama, Yüklü, Güncellemeler winget ile çalışmalı, hata gösterilmemeli. (Görev 1: yedek dal birim testi `Store_failure_falls_back_to_winget`.)
2. **Aynı uygulama iki kaynakta:** her kart kendi kaynağından kurulmalı. (Görev 1 gerçek test: Store kimliği yalnız msstore'da bulunur; winget kimliği winget'te.)
3. **Eski ayar dosyaları:** `LicenseNoticeShown: true` dosyaları bozulmadan okunmalı, güncel not bir kez gösterilmeli. (Görev 3 testleri.)
4. **Eski liste dosyaları:** yalnız `winget` kaynaklı eski dosyalar yüklenmeli. (Görev 2 testi.)
5. **Dil:** Store etiketi ve yeni not metni iki dilde. (TR/EN eşliği ve Görev 3 testi.)

---

### Task 1: Store kataloğu (winget bağlantısı)

**Files:** Modify `src/KolayKur.Core/Models/Package.cs`, `src/KolayKur.Core/Winget/ComWingetClient.cs`, `src/KolayKur.App/App.xaml.cs` (istemciye günlük verilirse); Test `tests/KolayKur.Core.Tests/Winget/ComWingetClientRealTests.cs` ve yedek dal için birim testi.

**Interfaces:**
- Produces: `PackageInfo(string Id, string Name, string Publisher, string? InstalledVersion, string? AvailableVersion, string? Source = null)`; `public static class PackageSources { public const string Winget = "winget"; public const string MicrosoftStore = "msstore"; }` (Models).

- [ ] **Step 1: API doğrulaması** — `PredefinedPackageCatalog.MicrosoftStore`, kaynak koşulu kabul özelliği (`AcceptSourceAgreements` ya da karşılığı), paket kaynak adını okuma yolu (`PackageVersionInfo.PackageCatalog.Info.Name` ya da karşılığı) derlenerek doğrulanır, rapora yazılır.
- [ ] **Step 2: Testler** (Real olanlar `[Trait("Category","Real")]`, yalnız okur)

```csharp
[Fact] public async Task Search_finds_store_app()          // SearchAsync("netflix") → bir sonucun Source == "msstore" ve Name "Netflix" içerir
[Fact] public async Task Search_marks_winget_source()      // SearchAsync("git") → Id "Git.Git" sonucunun Source == "winget"
[Fact] public async Task Unknown_store_id_is_not_found()   // InstallAsync("9ZZZZZZZZZZZ") → NotFound (hiçbir şey kurulmaz)
[Fact] public async Task Installed_items_carry_source()    // GetInstalledAsync → en az bir öğe Source == "winget"
[Fact] public async Task Store_failure_falls_back_to_winget() // birim: sahte bağlama işlevi Store denemesinde istisna atar → yalnız winget ile ikinci deneme yapılır, sonuç döner, günlükte bir kayıt
```

- [ ] **Step 3: Çalıştır → FAIL** — `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~ComWingetClient"`
- [ ] **Step 4: Uygula.** Arama: iki uzak + yüklü, `RemotePackagesFromAllCatalogs`; yüklü/güncelleme/kaldırma: `LocalCatalogs` ile iki uzak; kurma: iki uzak katalogun bileşiğinde kesin kimlik. "Önce iki kaynak, olmazsa yalnız winget" sırası katalog bağlama işlevini parametre alan küçük bir iç yardımcıda (test edilebilir). İstemciye isteğe bağlı `FileLog? log = null` eklenir, kurulumda verilir. `ToInfo` kaynağı doldurur.
- [ ] **Step 5: Çalıştır → PASS**; `Category!=Real` bütünü ve `Category=Real` (kapılı kurulum testi atlanır).
- [ ] **Step 6: Commit** — `feat: aramaya ve kurmaya Microsoft Store kaynagi`

### Task 2: Liste dosyasında kaynaklar

**Files:** Modify `src/KolayKur.Core/Lists/PackageListFile.cs`, `src/KolayKur.Core/ViewModels/InstalledViewModel.cs`; Test `tests/KolayKur.Core.Tests/Lists/PackageListFileTests.cs`, `ViewModels/InstalledViewModelTests.cs`.

**Interfaces:**
- Consumes: Task 1 `PackageInfo.Source`, `PackageSources`.
- Produces: `PackageListFile.IsExportable(PackageInfo package) : bool` (kaynak winget/msstore ve kimlik `ARP\`/`MSIX\` ile başlamıyor); `Save` imzası aynı, kaynak başına bir `Sources` girdisi; `Load` aynı.

- [ ] **Step 1: Gerçek biçim** — geçici klasöre `winget export -o <geçici> --accept-source-agreements --disable-interactivity`; msstore `SourceDetails` (Argument, Identifier, Name, Type) not edilir; dosya silinir, depoya girmez.
- [ ] **Step 2: Testler**

```csharp
[Fact] public void Save_writes_one_source_entry_per_source()   // Git.Git(winget) + 9WZDNCRFJ3TJ(msstore) → iki Sources girdisi, doğru SourceDetails ve kimlikler
[Fact] public void Load_reads_ids_from_all_sources()            // iki kaynaklı dosya → iki kimlik
[Fact] public void Load_still_reads_old_single_source_files()   // Samples/winget-export.json → 3 kimlik
[Fact] public void Unknown_source_is_not_exportable()           // Source null/"other" → false; ARP\ ve MSIX\ → false
[Fact] public void Export_includes_store_apps()                 // InstalledViewModel: winget + msstore yazılır, kaynaksızlar yazılmaz; sayı mesajı yazılan sayı
```

- [ ] **Step 3–5:** FAIL → uygula → PASS — `dotnet test tests/KolayKur.Core.Tests --filter "FullyQualifiedName~Lists|FullyQualifiedName~InstalledViewModel"`
- [ ] **Step 6: Gerçek winget denetimi** — `winget list --source msstore --accept-source-agreements --disable-interactivity` ile bu bilgisayarda kurulu bir Store uygulaması seç; programın `Save`'iyle (depoya girmeyen geçici kodla) `Git.Git` + o kimlikten liste yaz; `winget import -i <dosya> --ignore-unavailable --no-upgrade --accept-source-agreements --accept-package-agreements --disable-interactivity`. Beklenen: biçim hatası yok, ikisi de "zaten kurulu", hiçbir şey kurulmaz. Çıktı rapora.
- [ ] **Step 7: Commit** — `feat: liste dosyasi winget ve Store kaynaklarini ayri yazar`

### Task 3: Store etiketi, lisans notu, README

**Files:** Modify `src/KolayKur.Core/Support/AppSettings.cs`, `ViewModels/ShellViewModel.cs`, `src/KolayKur.App/Views/SearchPage.xaml`, `UpdatesPage.xaml`, `InstalledPage.xaml`, `Themes/Styles.xaml`, resx, `README.md`; Test `Support/AppSettingsTests.cs`, `ViewModels/ShellViewModelTests.cs`.

**Interfaces:**
- Consumes: Task 1 `PackageInfo.Source`.
- Produces: `AppSettings` son parametre `int LicenseNoticeVersion = 0`; `ShellViewModel.CurrentLicenseNoticeVersion = 2` (public const); `StoreChipStyle`.

- [ ] **Step 1: Testler**

```csharp
[Fact] public async Task License_notice_shows_for_new_users()           // ayar yok → Inform 1 kez; kayıtta LicenseNoticeVersion 2
[Fact] public async Task Updated_notice_shows_once_for_old_users()      // {"LicenseNoticeShown":true} → Inform 1 kez; ikinci StartAsync → yok
[Fact] public async Task Current_notice_version_is_not_shown_again()    // LicenseNoticeVersion 2 → Inform yok
[Fact] public void Notice_mentions_store_in_both_languages()            // Loc["License_Notice"] tr ve en'de "Microsoft Store" geçer (dil finally'de tr)
```

- [ ] **Step 2–4:** FAIL → uygula → PASS — `--filter "FullyQualifiedName~AppSettings|FullyQualifiedName~ShellViewModel|FullyQualifiedName~Strings"`. `License_Notice`'e eklenecek cümle: "Microsoft Store'dan kurmak, Microsoft Store kullanım koşullarını kabul etmek anlamına gelir." / "Installing from the Microsoft Store means accepting the Microsoft Store terms." `LicenseNoticeShown` geriye uyum için okunmaya devam eder.
- [ ] **Step 5: Store etiketi** — `Source == "msstore"` kartlarında (Ara, Güncellemeler, Yüklü) ad satırının altında küçük çip `Source_MicrosoftStore` "Microsoft Store"/"Microsoft Store", `StoreChipStyle` (DynamicResource; ikincil yazı ve kart çizgisi tonları, marka rengi değil). winget kartlarında çip yok.
- [ ] **Step 6: README** — "Neler yapar"a "Microsoft Store uygulamaları (örneğin Netflix) da aranır ve kurulur; ücretli Store uygulamaları kurulamaz" ve İngilizce karşılığı; "Denenenler"e "Store'dan gerçek kurulum: henüz denenmedi" satırı.
- [ ] **Step 7: Doğrula** — build 0 uyarı; testler; yayın exe'si ile `t9-search-netflix-light.png`, `t9-search-netflix-dark.png` (`-Search netflix`, yalnız arama).
- [ ] **Step 8: Commit** — `feat: Store etiketi, guncel lisans notu ve README`
