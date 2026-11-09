# KolayKur Uygulama Simgeleri Planı (tasarım + uygulama)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Kartlarda harf yerine uygulamanın gerçek simgesi görünsün; bulunamazsa harf kalsın.

**Onay:** Proje sahibi 9 Kasım 2026'da sohbette onayladı: Store logoları + kurulu programların kendi simgeleri + kurulu olmayan winget uygulamaları için katalog simgesi, yoksa uygulamanın kendi sitesindeki simge; hepsi yayından önce.

**Architecture:** Core'da WPF'ten bağımsız bir `IconService` simgeyi bulur, indirir, doğrular ve diske önbelleğe alır; sonuç yerel bir PNG yolu. `PackageAvatarView` önce harfi gösterir, simge hazır olunca onu gösterir. Simge ipuçları (katalog simge adresleri, uygulama/yayıncı sitesi, kurulu program simgesi) winget COM'dan istek üzerine, kart görününce alınır.

**Dal:** `store-destegi` (yayın öncesi son iş).

## Kaynak sırası (bir paket için)

1. **Katalog simgesi:** `PackageVersionInfo.GetCatalogPackageMetadata().Icons` (Store paketlerinde genellikle var; winget manifestlerinde nadiren). En uygun çözünürlük (≥ 64 px tercih), açık/koyu temaya uygunsa o.
2. **Kurulu programın simgesi:** paket kuruluysa yerelden: MSIX paketleri için paket logosu (Windows paket bilgisi), masaüstü programları için kaldırma kaydındaki `DisplayIcon` ya da kurulu exe'nin simgesi. İnternet gerekmez.
3. **Uygulamanın sitesi:** katalogdaki `PackageUrl`, yoksa `PublisherUrl` alanının ana makinesi: önce `https://<host>/apple-touch-icon.png`, sonra `https://<host>/favicon.ico`. Aracı servis kullanılmaz.
4. **Hiçbiri:** bugünkü harf avatarı.

## Global Constraints

- Yeni görünüm ve Store planlarının "Global Constraints" bölümleri geçerli (DynamicResource, iki dil, erişilebilirlik, 0 uyarı, testler).
- Yalnız `https`; `http`'ye yönlendirme reddedilir; en çok 512 KB; 5 sn zaman aşımı; aynı anda en çok 4 indirme.
- İndirilen veri resim olarak çözülebilmeli (PNG/ICO/JPEG/BMP); çözülemeyen ya da 16 px'ten küçük olan atılır. Hiçbir indirilen dosya çalıştırılmaz.
- Önbellek `%LocalAppData%\KolayKur\icons` (`AppPaths` altında yeni `Icons` yolu); dosya adı paket kimliği + kaynağın SHA-256 özetinden; "simge yok" sonucu da 7 gün önbelleğe alınır; toplam önbellek 50 MB'ı aşarsa en eskiler silinir.
- Simge bulma arayüzü dondurmaz (arka planda, `ConfigureAwait(false)`), kart kaydırılıp gizlenince iptal edilir.
- Bu makinede hiçbir uygulama kurulmaz/kaldırılmaz.
- README'de gizlilik notu: "KolayKur simgeleri Microsoft Store'dan ve uygulamaların kendi sitelerinden indirir; bu siteler bağlantıyı görür."

## Review Focus

1. **Kötü ya da büyük yanıt:** HTML dönen favicon adresi, 5 MB dosya, bozuk resim, http'ye yönlendirme → harf kalmalı, program donmamalı. (Birim testleri.)
2. **Çevrimdışı:** internet yokken arayüz takılmamalı, harf görünmeli, hata gösterilmemeli. (Birim testi: ağ hatası → harf.)
3. **Çok kart:** 120 kartlık Yüklü sayfasında sayfa akıcı kalmalı; aynı simge iki kez indirilmemeli. (Eşzamanlılık sınırı ve önbellek testi.)
4. **Tema:** şeffaf simgeler koyu temada da görünür olmalı (kart zemini üstünde). (Ekran görüntüsü.)
5. **Önbellek bozulması:** yarım/bozuk önbellek dosyası → yeniden indirilir ya da harf. (Birim testi.)

---

### Task 1: Uygulama simgeleri

**Files:** Create `src/KolayKur.Core/Icons/IconService.cs`, `IconHints.cs`, `IconCache.cs` (+ gerekiyorsa küçük yardımcılar); Modify `src/KolayKur.Core/Winget/IWingetClient.cs` + `ComWingetClient.cs` + `tests/.../Fakes/FakeWingetClient.cs` (ipucu okuma), `src/KolayKur.Core/Support/AppPaths.cs` (`Icons`), `src/KolayKur.App/Views/PackageAvatarView.xaml(.cs)`, `src/KolayKur.App/App.xaml.cs`, `README.md`; Tests `tests/KolayKur.Core.Tests/Icons/*`.

**Interfaces:**
- Produces: `public sealed record IconHints(IReadOnlyList<Uri> CatalogIcons, Uri? Website, string? InstalledIconSource);` `IWingetClient.GetIconHintsAsync(string packageId, string? source, CancellationToken ct) : Task<IconHints>`; `public sealed class IconService { Task<string?> GetIconPathAsync(PackageInfo package, CancellationToken ct); }` (yerel PNG yolu ya da `null`). İndirme ve yerel simge çıkarma, testte sahtelenebilen küçük arayüzlerle (`IIconFetcher`, `IInstalledIconReader`) yapılır; gerçekleri App ya da Core'da Windows'a özgü sınıflarda.

- [ ] **Step 1: API doğrulaması** — `GetCatalogPackageMetadata`, `Icons` (Url, FileType, Resolution, Theme), `PackageUrl`/`PublisherUrl`, kurulu sürümden yerel simge bilgisi (`InstalledLocation`, ürün kodları vb.) derlenerek doğrulanır, rapora yazılır.
- [ ] **Step 2: Testler (önce)** — `Uses_catalog_icon_first`, `Falls_back_to_installed_icon`, `Falls_back_to_website_icon`, `Returns_null_when_nothing_found_and_caches_negative`, `Rejects_http_and_http_redirects`, `Rejects_non_image_and_oversized`, `Network_failure_returns_null_without_throwing`, `Same_icon_downloaded_once_under_concurrency`, `Corrupt_cache_file_is_replaced`, `Cache_is_trimmed_above_limit`.
- [ ] **Step 3: FAIL → uygula → PASS** (`--filter "FullyQualifiedName~Icons"`), 10 döngü.
- [ ] **Step 4: Arayüz** — `PackageAvatarView` harfi hemen gösterir; görünür olunca `IconService`'ten yol ister, gelirse 40×40 (köşe 10 px) resmi gösterir; görünmez olunca isteği iptal eder. Bütün kartlarda (Ara, Güncellemeler, Yüklü) çalışır.
- [ ] **Step 5: README** — gizlilik notu (TR+EN), özellik satırına "uygulama simgeleri".
- [ ] **Step 6: Doğrula** — build 0 uyarı; `Category!=Real` ve yalnız okuyan Real testler; yayın exe'si ile ekran görüntüleri `t10-search-git-light.png`, `t10-search-netflix-dark.png`, `t10-installed-light.png`, `t10-installed-dark.png`; README görselleri (`docs/images/*.png`) simgeli hâlleriyle yenilenir.
- [ ] **Step 7: Commit** — `feat: kartlarda uygulama simgeleri`
