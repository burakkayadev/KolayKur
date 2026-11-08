# KolayKur Tasarım Belgesi

Nov 7, 2026 · @Burak Can Kaya

KolayKur, winget'i terminal açmadan kullandıran bir Windows programıdır. C# ve WPF ile yazılacak, tek `.exe` olarak GitHub'dan dağıtılacak. Bilgisayara format atılacağı için bu belge işe devam etmek için gereken her şeyi içerir.

## Format sonrası başlangıç

Format bu belgeyi etkilemez, çünkü belge Claude hesabında durur. Bilgisayarda henüz proje dosyası yoktur.

1. Claude masaüstü uygulamasını kur ve aynı hesapla giriş yap.
2. Claude yedeğini C:\\yedek klasörüne çıkar ve geri-yukle.cmd dosyasına çift tıkla. Sonra yeni bir oturum aç ve proje için bir klasör seç.
3. Bu belgenin linkini yapıştır ve "plana göre başlayalım" yaz.

**Claude için not:** İşe başlamadan önce aşağıdaki yazılımları kontrol et, eksik olanların hepsini kur. Proje sahibi bunu önceden istedi. Önce `winget --version` komutunu dene. Çalışmıyorsa proje sahibinden Microsoft Store'da "Güncellemeleri al" düğmesine basmasını iste.

| Yazılım | Ne için | Kurulum |
| --- | --- | --- |
| Git | Kod geçmişi ve GitHub | `winget install --id Git.Git -e` |
| GitHub CLI | Depo açma ve sürüm yükleme | `winget install --id GitHub.cli -e` |
| .NET 10 SDK | Programı derleme ve test etme | `winget install --id Microsoft.DotNet.SDK.10 -e` |
| Windows Sandbox | Winget'in olmadığı durumu deneme | Proje sahibi Windows Özellikleri'nden açar, yeniden başlatma gerekebilir |

GitHub girişini (`gh auth login`) proje sahibi kendisi yapar.

**Windows kişiselleştirme:** Proje sahibi formattan sonra Windows'u Claude ile birlikte kişiselleştirmek istiyor. Yeni oturumda KolayKur'a geçmeden önce bunu sor.

## Amaç ve kapsam

KolayKur, uygulama kurmayı ve güncellemeyi terminal gerektirmeden tek yerden yaptırır. Kullanıcılar proje sahibi ve arkadaşlarıdır. Program GitHub'dan indirilir ve ek kurulum istemeden açılır.

**İlk sürümde olanlar**

- Uygulama arama ve tek tıkla kurma
- Birkaç uygulamayı seçip toplu kurma
- Güncellemeleri listeleme, tek tek veya hepsini birden güncelleme
- Yüklü uygulamaları listeleme ve kaldırma
- Uygulama listesini dosyaya kaydetme ve o listeden kurma
- Türkçe ve İngilizce arayüz
- Winget yoksa veya bozuksa kurma ve onarma

**İlk sürümde olmayanlar**

- Programın kendini güncellemesi
- ARM işlemcili bilgisayarlar
- Kod imzalama sertifikası
- Winget dışındaki paket yöneticileri

**Başarı ölçütleri**

- Kullanıcı hiçbir işlem için terminal açmaz.
- `.exe`, .NET kurulu olmayan temiz bir Windows 11'de açılır.
- Winget'in olmadığı Windows Sandbox'ta program winget'i kurar ve arama yapar.

Deneme ortamı 64 bit Windows 11'dir. Program Windows 10'da çalışabilir, ama ilk sürümde orada denenmez.

## Ekranlar

Programın solunda dört sayfalı bir menü, altında işlem listesi bulunur.

| Sayfa | Ne gösterir | Düğmeler |
| --- | --- | --- |
| Ara | Arama sonuçları: ad, yayıncı, sürüm | Kur, Seçilenleri kur |
| Güncellemeler | Güncellemesi olan uygulamalar: ad, kurulu sürüm, yeni sürüm | Güncelle, Tümünü güncelle |
| Yüklü uygulamalar | Bilgisayardaki uygulamalar | Kaldır, Listeyi dışa aktar, Listeden kur |
| Ayarlar | Dil seçimi ve winget durumu | Onar, Günlüğü aç |

- **İşlem listesi:** Kurulum, güncelleme ve kaldırma işleri sırayla çalışır. Her işin ilerlemesi ve sonucu görünür.
- **Açılış kontrolü:** Winget çalışmıyorsa diğer sayfalar açılmadan önce onarım ekranı gelir.
- **Kaldırma onayı:** "Kaldır" düğmesi işlemden önce onay ister.
- **Yönetici izni:** Bir kurulum izin isterse Windows'un kendi izin penceresi çıkar.

## Teknik yapı

Program C#, WPF ve .NET 10 ile yazılır. Winget'e Microsoft'un resmi COM arayüzüyle bağlanır, `winget` komutunun çıktısını okumaz. Arayüz paketi `Microsoft.WindowsPackageManager.ComInterop` 1.29.380'dir ve MIT lisanslıdır.

Komutun çıktısı ekran için hazırlanmış bir tablodur. Uzun adlar kesilebilir, başlıklar Windows diline göre değişir. COM arayüzü ise veriyi düzgün verir ve kurulumun ilerlemesini bildirir.

&#91;embedded content: KolayKur parçaları ve bağlantıları\]

Ekranlar arama ve listeleme için bağlantıyı doğrudan kullanır. Kurma, güncelleme ve kaldırma işlem sırasından geçer.

`IWingetClient` altı işlem sunar: `SearchAsync`, `GetInstalledAsync`, `GetUpgradesAsync`, `InstallAsync`, `UpgradeAsync`, `UninstallAsync`. Testlerde bu arayüzün sahte sürümü kullanılır.

| Konu | Karar | Neden |
| --- | --- | --- |
| Çalışma ortamı | .NET 10, SDK 10.0.403 | Uzun süreli destek sürümü, desteği 14 Kasım 2028'e kadar sürüyor |
| Proje hedefi | `net10.0-windows10.0.26100.0` | COM paketi `net8.0-windows10.0.26100` hedefini sunuyor |
| Ekran kodu | WPF ve MVVM | Ekran kodu işten ayrı kalır, iş kodu test edilebilir |
| Yetki | Normal kullanıcı | Yönetici izni yalnızca gereken kurulumda istenir |
| İşlemci | 64 bit Intel/AMD (`win-x64`) | ARM ilk sürümde kapsam dışı |
| Kayıt | `%LocalAppData%\KolayKur` altında ayar ve günlük dosyaları | Veritabanına gerek yok |

**Doğrulanan:** Arama, yüklü uygulamalar, güncelleme kontrolü, kurma, güncelleme, kaldırma ve ilerleme bilgisi arayüzün kaynak kodunda var.

**Doğrulanmayan:** Winget bozuk olduğu için COM arayüzü çalıştırılarak denenmedi. Tek `.exe` içinde çalıştığı da görülmedi. Bu, yapım sırasındaki ilk denemedir. Olmazsa yalnızca `IWingetClient` komut çıktısını okuyacak şekilde yazılır.

## Winget kurulumu ve onarımı

Program açılışta winget'i gerçekten çalıştırarak dener, dosyanın var olmasına güvenmez. Arızalı bilgisayarda dosya da paket durumu da sağlam görünüyordu, ama winget çalışmıyordu.

1. **Deneme:** `winget --version` çalıştırılır. Başarılıysa program normal açılır.
2. **Teşhis:** Başarısızsa durum aşağıdaki tablodaki üç durumdan biri olarak belirlenir.
3. **Onay:** Kullanıcı nedeni görür ve "Onar" düğmesine basar.
4. **Onarım:** Gereken dosyalar indirilir, doğrulanır ve kurulur. Yönetici izni gerekirse Windows sorar.
5. **Tekrar deneme:** Winget yeniden çalıştırılır. Yine başarısızsa açık bir mesaj gösterilir ve günlüğe yazılır.

| Durum | Nasıl anlaşılır | Onarım |
| --- | --- | --- |
| Kurulu değil | App Installer paketi yok | Bağımlılıkları ve App Installer paketini kur |
| Bileşen eksik veya takılı | Paketin istediği bir bileşen kullanıcıda kayıtlı değil | Bileşeni resmi kaynaktan kur, paketi yeniden kaydet |
| Kayıt bozuk | Paket ve bileşenler tamam, winget çalışmıyor | Paketi yeniden kaydet |

Program sürüm numarası ezberlemez, dosyaları her seferinde winget-cli'nin son sürümünden alır. Bu dosyalar yönetici izni olmadan kurulabilir, bu beklenti Windows Sandbox'ta doğrulanır.

| Dosya | Kaynak | Boyut (7 Kasım 2026) |
| --- | --- | --- |
| `DesktopAppInstaller_Dependencies.zip` | [winget-cli v1.29.380](https://github.com/microsoft/winget-cli/releases/tag/v1.29.380) | 93,2 MB |
| `Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle` | [winget-cli v1.29.380](https://github.com/microsoft/winget-cli/releases/tag/v1.29.380) | 207,2 MB |

Dosyalar yalnızca bu resmi kaynaklardan alınır. Boyutu veya SHA-256 özeti tutmayan dosya silinir ve kurulmaz. Özetler sürüm bilgisinden okunur, paket imzalarını kurulumda Windows da denetler.

### Bu bilgisayardaki arızanın kaydı

Format bu izleri sileceği için arıza burada kayıtlıdır. Winget, Store'un yarıda kalan indirmesi yüzünden çalışmıyor.

| Tarih | Olay |
| --- | --- |
| 7 Kasım 2026 20:41 | Son kayıt denemesi de aynı hatayla başarısız oldu, toplam 72 deneme |
| 31 Ekim 2026 15:40 | İlk kayıt hatası: `0x80073CF3`, bileşen "paused state" durumunda |
| 31 Ekim 2026 15:38 | `WindowsAppRuntime.1.8` x64 indirmesi yarıda kaldı, yaklaşık 14,4 MB'ın 6,4 MB'ı yazıldı |
| 31 Ekim 2026 15:38 | Aynı bileşenin x86 sürümü sorunsuz kuruldu, App Installer 1.29.379.0 klasörü oluştu |

- **Görülen hata:** `winget --version` komutu "Sistem dosyaya erişemiyor" diyor.
- **Bulgu:** `winget.exe` kısayolu hâlâ 1.26.509.0 sürümünü gösteriyor, kurulu paket 1.29.379.0. Diğer uygulamaların sürümü okunabilen kısayolları kurulu sürümle eşleşiyor.
- **Çıkarım:** Takılı bileşenin tam olarak bu hata metnini ürettiği doğrulanmadı.
- **Ders:** Paketi yeniden kaydetmek tek başına yetmez, 72 kayıt denemesi aynı nedenle başarısız oldu.
- **Denenmeyen:** Bileşen paketini elle kurmanın takılı bileşeni temizleyip temizlemediği bilinmiyor.

## Hatalar

Bir iş başarısız olduğunda program durmaz, nedeni gösterir ve sıradaki işe geçer.

| Durum | Programın davranışı |
| --- | --- |
| Kurulum, güncelleme veya kaldırma başarısız | İş "başarısız" işaretlenir, neden gösterilir, sıradaki işler devam eder |
| Toplu iş bitti | Kaç işin başarılı, kaçının başarısız olduğu özetlenir |
| Yönetici izni reddedildi | Yalnızca o iş iptal olur |
| İnternet yok | Açık bir mesaj ve "Tekrar dene" düğmesi gösterilir |
| Onarım dosyası yarım veya bozuk | Dosya silinir, kurulmaz, tekrar indirme önerilir |
| Liste dosyası okunamıyor | Hata gösterilir, hiçbir kurulum başlamaz |
| Listedeki bir uygulama bulunamıyor | O uygulama "bulunamadı" işaretlenir, diğerleri kurulur |

Program yaptıklarını günlük dosyasına yazar, Ayarlar'daki "Günlüğü aç" düğmesi bu dosyayı açar. "Kur" düğmesine basmak o uygulamanın lisansını kabul etmek anlamına gelir. Program bunu ilk açılışta bir kez söyler.

## Test

İş kodu otomatik testlerle, winget'e bağlı her şey gerçek denemelerle sınanır.

**Otomatik testler** xUnit ve sahte `IWingetClient` ile çalışır, gerçek kurulum yapmaz.

- **Teşhis:** Her arıza durumu doğru sınıflanır.
- **İşlem sırası:** Başarısız bir iş sonrakileri durdurmaz.
- **Liste dosyası:** Yazılan dosya okununca aynı liste çıkar, bozuk dosya reddedilir.

**Gerçek denemeler**

| Ortam | Ne denenir |
| --- | --- |
| Temiz Windows 11 | Normal yol: arama, küçük bir uygulamayı kurma, güncelleme ve kaldırma |
| Windows Sandbox | Winget'in olmadığı durum: program winget'i kurar ve arama yapar |
| Terminal | Programın ürettiği liste dosyası `winget import` ile okunur |
| Bir arkadaşın bilgisayarı | İndirme, Windows uyarısı ve ilk açılış |

[Windows Sandbox'ta winget de Store da yoktur](https://learn.microsoft.com/en-us/windows/package-manager/winget/) ve [her açılış sıfırdan başlar](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/). Bu yüzden kurulum yolu istenildiği kadar tekrar denenebilir. Bu özelliğin Windows'ta bir kez açılması gerekir.

Bu bilgisayardaki takılı bileşen arızası format sonrası yeniden oluşturulamaz. Bu durum yalnızca otomatik testlerde sahte cevaplarla sınanır.

## Dağıtım

Program tek `KolayKur.exe` dosyası olarak GitHub'ın Releases sayfasına konur. .NET içine gömülü gelir.

```bash
dotnet publish src/KolayKur.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/release
```

**Ölçüm (8 Kasım 2026):** `out/release/KolayKur.exe` 68,5 MB'dır. Sıkıştırma (`EnableCompressionInSingleFile`) uygulama projesinde açıktır. Sıkıştırmasız boyut 159,7 MB'dı. Dosya tek başına yeni bir klasöre kopyalanıp açıldığında ana pencere en kötü 3 saniyede geldi (sıkıştırmasızda 1,7 saniye), bu yüzden sıkıştırma kaldı.

**Winmd kararı (8 Kasım 2026):** Winget nesneleri `Microsoft.Management.Deployment.winmd` dosyası exe'nin yanında değilse oluşturulamaz (`0x80073D54`). Tek dosya paketine gömülen winmd diske yazılmaz. Bu yüzden winmd programa kaynak olarak gömülür ve program açılışta, winget'e ilk dokunuştan önce, yoksa exe'nin yanına çıkarır. Böylece "tek `.exe`" sözü korunur. Klasör yazılamazsa program bunu söyler ve `KolayKur.exe`'yi Masaüstü gibi yazılabilir bir klasöre taşımayı ister. Yayın klasörüne ayrı bir winmd konmaz.

- **Depo:** Arkadaşların hesap açmadan indirebilmesi için depo herkese açık olmalıdır. Yüklemeden önce proje sahibine sorulur.
- **Windows uyarısı:** Program imzasız olduğu için ilk açılışta "Windows bilgisayarınızı korudu" uyarısı çıkabilir. "Ek bilgi" ve "Yine de çalıştır" ile geçilir. Bu bilgi GitHub sayfasına yazılır.
- **Yeni sürümler:** Program kendini güncellemez, yeni sürüm GitHub'dan elle indirilir.

## Yapım sırası

Kod yazımı formattan sonra başlar. Parçaların çoğu çalışan winget ister, kurulum yolu ise Windows Sandbox'ta her zaman denenebilir.

1. **Hazırlık:** .NET 10 SDK kurulur ve GitHub deposu açılır. GitHub girişini proje sahibi yapar. Kod ilk günden GitHub'da tutulur.
2. **COM denemesi:** Küçük bir program tek `.exe` içinde winget'e bağlanıp arama yapar. Çalışmazsa yedek yola geçilir.
3. **Winget kontrolü ve onarımı:** Yazılır ve Windows Sandbox'ta denenir.
4. **Winget bağlantısı ve işlem sırası:** Otomatik testlerle yazılır.
5. **Ekranlar:** Ara, Güncellemeler, Yüklü uygulamalar ve Ayarlar sayfaları yapılır.
6. **Liste dosyası ve diller:** Dışa aktarma, listeden kurma, Türkçe ve İngilizce eklenir.
7. **Paketleme ve sürüm:** Gerçek denemeler yapılır, `.exe` GitHub'a konur.

Görev görev ayrıntılar: Uygulama planı

## Açık kararlar

Kod yazımı formattan sonra başlayacak ve programın adı KolayKur olarak kaldı. Tasarım kararlarını proje sahibi Claude'a bıraktı. Geriye tek karar kaldı:

- [ ] GitHub deposu herkese açık olacak mı? Arkadaşların hesapsız indirmesi için gerekir, yüklemeden önce sorulacak.

## Kaynaklar

- [winget-cli v1.29.380 sürümü](https://github.com/microsoft/winget-cli/releases/tag/v1.29.380)
- [Winget COM arayüzü kaynak kodu](https://raw.githubusercontent.com/microsoft/winget-cli/master/src/Microsoft.Management.Deployment/PackageManager.idl)
- [Microsoft.WindowsPackageManager.ComInterop paketi](https://www.nuget.org/packages/Microsoft.WindowsPackageManager.ComInterop/1.29.380)
- [.NET sürüm bilgisi](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json)
- [WinGet belgeleri](https://learn.microsoft.com/en-us/windows/package-manager/winget/)
- [Windows Sandbox belgeleri](https://learn.microsoft.com/en-us/windows/security/application-security/application-isolation/windows-sandbox/)
