# KolayKur

KolayKur, Windows'un paket yöneticisi winget'i terminal açmadan kullandıran küçük bir programdır. Uygulama arar, kurar, günceller ve kaldırır. Hepsi pencereden yapılır, komut yazmak gerekmez.

Program tek bir `KolayKur.exe` dosyasıdır. Tasarım gereği ek kurulum istemez ve .NET çalışma ortamı içine gömülü gelir, yani .NET kurmanız gerekmemelidir. Bu, temiz bir makinede henüz denenmedi (aşağıdaki tabloya bakın).

## Neler yapar

- Uygulama arama ve tek tıkla kurma
- Birkaç uygulamayı seçip toplu kurma
- Güncellemeleri listeleme, tek tek veya hepsini birden güncelleme
- Yüklü uygulamaları listeleme ve kaldırma (kaldırmadan önce onay sorar)
- Uygulama listesini dosyaya kaydetme ve o listeden kurma
- Türkçe ve İngilizce arayüz
- Winget yoksa veya bozuksa teşhis edip kurma ve onarma ("Onar" düğmesi); onarım akışı henüz gerçek bozuk bir winget üzerinde denenmedi

**Önemli:** Yukarıdaki kurma, güncelleme ve kaldırma işlevleri yazıldı ve sahte winget ile otomatik testlerde sınandı. Ancak programdan **gerçek bir kurma, güncelleme veya kaldırma henüz denenmedi**; yalnızca var olmayan bir kimliğin "bulunamadı" sonucu verdiği gerçek winget ile doğrulandı. Ayrıntı için aşağıdaki "Denenenler ve denenmeyenler" tablosuna bakın.

Program normal kullanıcı olarak çalışır. Bir uygulamanın kurulumu yönetici izni isterse, tasarım gereği Windows'un kendi izin penceresi (UAC) çıkması beklenir. İzin penceresi ve "Hayır" yolu henüz denenmedi (aşağıdaki tabloya bakın).

**Lisans:** "Kur" düğmesine basmak, o uygulamanın lisansını kabul etmek anlamına gelir. Program bunu ilk açılışta hatırlatır.

## İndirme

1. Bu deponun **Releases** (Sürümler) sayfasından en son sürümün `KolayKur.exe` dosyasını indirin.
2. Dosyayı Masaüstü gibi yazılabilir bir klasöre koyup çift tıklayın.

Program ilk açılışta, çalışması için gereken küçük bir dosyayı (`Microsoft.Management.Deployment.winmd`, yaklaşık 64 KB) `KolayKur.exe`'nin yanına çıkarır. Bu yüzden exe'nin bulunduğu klasör yazılabilir olmalıdır. Yazılamıyorsa program bunu söyler. Exe'yi başka bir klasöre taşımanız yeterlidir.

### Windows uyarısı (SmartScreen)

Program **imzasızdır** (kod imzalama sertifikası yoktur). Bu yüzden ilk açılışta Windows şu uyarıyı gösterebilir: "Windows bilgisayarınızı korudu".

Geçmek için:

1. Uyarıdaki **Ek bilgi** bağlantısına tıklayın.
2. Görünen **Yine de çalıştır** düğmesine basın.

Programın kaynak kodu bu depodadır; isterseniz kendiniz derleyebilirsiniz (aşağıya bakın).

## Gereksinimler

- 64 bit (x64) Windows 11. ARM işlemcili bilgisayarlar kapsam dışıdır.
- Windows 10'da çalışması beklenir, ama **denenmedi**.
- Winget. Yoksa veya bozuksa program bunu algılar ve onarmayı önerir (onarım akışı henüz gerçek bozuk bir winget üzerinde denenmedi).

## Dosyalar nerede

Ayarlar ve günlük dosyaları `%LocalAppData%\KolayKur` klasöründedir (yani `C:\Users\<ad>\AppData\Local\KolayKur`). Günlük dosyalarını Ayarlar sayfasındaki "Günlüğü aç" düğmesiyle de açabilirsiniz. Winget onarımı sırasında indirilen dosyalar `%LocalAppData%\KolayKur\downloads` klasörüne gider ve onarım başarılı olunca silinir.

Programın kendisi iki yere daha dosya bırakır:

- `Microsoft.Management.Deployment.winmd`: ilk açılışta `KolayKur.exe`'nin **yanına** çıkarılır (bkz. İndirme).
- .NET tek dosya çıkarma klasörü: `%TEMP%\.net\KolayKur`. Tek dosyalık program, içine gömülü bazı dosyaları ilk çalışmada buraya açar. Silmek güvenlidir, program gerekirse yeniden oluşturur.

## Derleme ve sınama

Gereken: .NET 10 SDK.

Tek dosya yayını:

```bash
dotnet publish src/KolayKur.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/release
```

Sonuç `out/release/KolayKur.exe` dosyasıdır; dağıtılacak tek dosya budur (klasördeki `.pdb` dosyaları hata ayıklama içindir). Sıkıştırma (`EnableCompressionInSingleFile`) uygulama projesinde açıktır: dosya yaklaşık 69 MB olur, sıkıştırmasız yaklaşık 160 MB. Sıkıştırmasız üretmek isterseniz komuta `-p:EnableCompressionInSingleFile=false` ekleyin.

Testler:

```bash
dotnet test --filter "Category!=Real"
```

`Category=Real` testleri gerçek winget'e bağlanır. Bunlardan yalnızca bir tanesi makineye bir şey kurar ve kaldırır; o test yalnızca `KOLAYKUR_REAL_INSTALL=1` ortam değişkeni verilirse çalışır. Bu değişkeni yalnızca yazılımın kurulup kaldırılmasını göze alıyorsanız verin.

## Denenenler ve denenmeyenler

Dürüst durum notu:

| Konu | Durum |
| --- | --- |
| Otomatik testler (sahte winget ile) | Geçiyor |
| Bu geliştirme bilgisayarında tek `KolayKur.exe`'nin başka bir klasörde açılması | Denendi |
| Programın ürettiği liste dosyasının `winget import` ile okunması | Denendi (biçim hatası yok) |
| Temiz bir Windows 11 bilgisayarda (.NET kurulu değilken) açılması | Henüz denenmedi |
| Programdan gerçek kurma / güncelleme / kaldırma | Henüz denenmedi (yalnızca var olmayan kimliğin "bulunamadı" sonucu gerçek winget ile görüldü) |
| Programdan bozuk winget'in onarılması ("Onar") | Henüz denenmedi |
| UAC izin penceresi ve "Hayır" yolu | Henüz denenmedi |
| Windows Sandbox'ta winget'in onarılması | Henüz denenmedi |
| Windows 10 | Henüz denenmedi |
| Arkadaşların GitHub'dan indirip SmartScreen uyarısını geçmesi | Henüz denenmedi |

---

# English

KolayKur is a small Windows program that lets you use winget (the Windows package manager) without opening a terminal. You can search, install, update and uninstall apps from a window.

It ships as a single `KolayKur.exe`. By design it needs no extra installation and .NET is bundled inside, so you should not need to install .NET. This has not yet been tested on a clean machine (see "Not yet tested" below).

**Features:** search and one-click install; multi-select install; list updates and update one or all; list and uninstall installed apps (asks first); export your app list to a file and install from a list; Turkish and English UI; diagnoses and repairs a missing or broken winget (the repair flow has not yet been tried on a really broken winget).

**Important:** the install, update and uninstall features are written and covered by automated tests with a fake winget, but a **real install, update or uninstall from the program has not been tried yet**; only an unknown package ID returning "not found" was verified against the real winget. See "Not yet tested" below.

**Download:** get `KolayKur.exe` from the **Releases** page of this repository and run it from a writable folder such as the Desktop. On first start it extracts a small file (`Microsoft.Management.Deployment.winmd`, about 64 KB) next to the exe; if the folder is not writable the program tells you to move the exe.

**SmartScreen:** the program is **unsigned**, so Windows may show "Windows protected your PC". Click **More info**, then **Run anyway**.

**Requirements:** 64-bit Windows 11. Windows 10 is expected to work but has not been tested. ARM is out of scope.

**Files:** settings and logs are in `%LocalAppData%\KolayKur`; files downloaded during a winget repair go to `%LocalAppData%\KolayKur\downloads` and are deleted after a successful repair. The program also leaves two other things: the `Microsoft.Management.Deployment.winmd` file extracted next to `KolayKur.exe` on first run, and the .NET single-file extraction folder `%TEMP%\.net\KolayKur` (safe to delete; it is recreated when needed).

**Licenses:** pressing "Install" (Kur) means you accept that package's license.

**Administrator rights:** the program runs as a normal user. If an installer needs admin rights, Windows' own UAC prompt is expected to appear (that is Windows' behavior by design); the prompt and the "No"/cancel path have not been tested yet.

**Build:** see the `dotnet publish` command above. Run tests with `dotnet test --filter "Category!=Real"`. Tests marked `Real` use the real winget; the one that installs and uninstalls software only runs when `KOLAYKUR_REAL_INSTALL=1` is set.

**Not yet tested:** real install, update and uninstall from the program, repairing a broken winget from the program, a clean Windows 11 machine without .NET, the UAC prompt and the "No" path, the repair flow in Windows Sandbox, Windows 10, and downloads by friends through GitHub Releases.
