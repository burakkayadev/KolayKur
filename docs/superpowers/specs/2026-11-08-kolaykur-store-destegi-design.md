# KolayKur Microsoft Store desteği: tasarım belgesi

8 Kasım 2026 · Durum: proje sahibi sohbette onayladı · Temel: `yeni-tasarim` dalı (yeni görünüm)

## Amaç

KolayKur bugün yalnızca winget'in topluluk kataloğunu (`winget` kaynağı) kullanıyor. Netflix gibi yalnızca Microsoft Store'da olan uygulamalar aramada çıkmıyor. Hedef: aynı "ara, Kur'a bas" akışıyla Store uygulamalarını da kurabilmek.

**Başarı ölçütleri**

- "netflix" aramasında Netflix (Microsoft Store) sonucu çıkar ve "Kur" ile kurulur.
- winget sonuçları ve bütün bugünkü işlevler aynı çalışır; mevcut testler geçer.
- Store'a ulaşılamazsa arama winget sonuçlarıyla çalışmaya devam eder.

## Kapsam

**İçinde:** Store kaynağıyla arama, kurma, güncelleme, kaldırma ve yüklü listesi; Store kartlarında etiket; liste dosyasında Store bölümü; Store koşullarını da anan lisans notu.

**Dışında:** ücretli Store uygulamaları, Chocolatey/Scoop, doğrudan site indirmeleri, Microsoft hesabıyla oturum açma.

## Davranış

- **Arama:** iki uzak katalogda birden (`winget`, `msstore`), yüklü katalogla birleştirilerek. Her sonuç kaynağını taşır; Store kartlarında küçük "Microsoft Store" etiketi, winget kartlarında etiket yok. Aynı uygulama iki katalogda varsa iki kart. Sonuç sınırı 50.
- **Store'a bağlanılamazsa:** arama, Yüklü ve Güncellemeler yalnız winget ile çalışır; hata günlüğe yazılır, kullanıcıya hata gösterilmez. winget'e de bağlanılamazsa bugünkü davranış sürer.
- **Kurma:** kimlik iki uzak katalogda aranır, paket geldiği kaynaktan kurulur (Store kimlikleri `9WZDNCRFJ3TJ` gibi 12 karakterlidir, winget kimlikleriyle çakışmaz). Store kaynağının koşulları programca kabul edilir. Ücretli ya da hesap isteyen kurulum bugünkü "Kurulum başarısız" nedeniyle biter.
- **Yüklü ve Güncellemeler:** yüklü katalog iki uzak katalogla birleştirilerek okunur; Store'dan kurulanlar Store kimliği ve kaynağıyla görünür.
- **Liste dosyası:** winget biçiminde, her kaynak için ayrı `Sources` girdisi (`winget`, `msstore`); `SourceDetails` gerçek `winget export` çıktısıyla aynı. Kaynağı bilinmeyenler (`ARP\`, `MSIX\`, eşleşmeyenler) yazılmaz. Listeden kurma iki kaynağın kimliklerini de kurar.
- **Lisans notu:** Store'dan kurmanın Microsoft Store koşullarını kabul etmek olduğunu da söyler; notu daha önce görmüş kullanıcılara güncel not bir kez gösterilir (not sürümü ayar dosyasında).

## Teknik yapı

- `PackageInfo`'ya isteğe bağlı son parametre `string? Source = null` (`"winget"`, `"msstore"`, `null`).
- `ComWingetClient`: `PredefinedPackageCatalog.OpenWindowsCatalog` ve `PredefinedPackageCatalog.MicrosoftStore`; Store başvurusunda kaynak koşulları kabul edilir; kaynak adı paketin sürüm bilgisindeki katalog adından okunur; Store bağlantısı başarısızsa yalnız winget ile yeniden bağlanılır.
- `PackageListFile` kaynaklara göre gruplar; `IsExportable` kaynağa bakar.
- `AppSettings`'e `int LicenseNoticeVersion` (geçerli 2; eski `LicenseNoticeShown = true` sürüm 1 sayılır).
- Yeni metinler iki dile birden.

## Test ve doğrulama

- Gerçek winget ile yalnız okuyan testler: "netflix" → kaynağı `msstore` olan sonuç; "git" → `Git.Git` kaynağı `winget`; var olmayan Store biçimli kimlik "bulunamadı".
- Liste dosyası birim testleri ve gerçek `winget import` denetimi yalnız zaten kurulu bir Store uygulaması ve `Git.Git` ile (`--no-upgrade --ignore-unavailable`; hiçbir şey kurulmaz).
- Lisans notu sürümü birim testleri; "netflix" araması ekran görüntüleri.
- Bu bilgisayarda hiçbir uygulama kurulmaz; Netflix'i gerçekten kurmayı proje sahibi dener.

## Riskler

Store kataloğu bazı ağ ya da bölgelerde açılmayabilir (winget ile devam); Store koşulları ilk kullanımda kabul ister (programca kabul, lisans notunda söylenir); COM adları ilk görevde derlenerek doğrulanır.
