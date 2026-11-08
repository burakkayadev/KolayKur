# KolayKur Uygulama Planı

> **Uygulayanlar için:** Planı `superpowers:executing-plans` veya `superpowers:subagent-driven-development` ile görev görev uygula. Başlamadan önce proje sahibine yöntemi sor: kendin mi yapacaksın, Sonnet alt ajanı mı? Model seçimini, nedenini ve tahmini token miktarını söyle, onay bekle.

**Amaç:** Terminal gerektirmeden winget ile uygulama arayan, kuran, güncelleyen ve kaldıran tek `.exe` WPF programı yapmak.

**Mimari:** `KolayKur.Core` iş kodunu ve ViewModel'leri tutar, WPF'e bağlı değildir. `KolayKur.App` yalnızca XAML ekranlarıdır. Winget'e `IWingetClient` üzerinden COM ile bağlanılır, testlerde sahtesi kullanılır.

**Teknolojiler:** C#, .NET 10, WPF, CommunityToolkit.Mvvm, xUnit, `Microsoft.WindowsPackageManager.ComInterop`.

**Tasarım:** KolayKur-Tasarim.md

## Genel kurallar

- Hedef çatı `net10.0-windows10.0.26100.0`, çalışma kimliği `win-x64`.
- .NET SDK 10.0.403 veya daha yeni bir 10.0.x sürümü kullanılır.
- `Microsoft.WindowsPackageManager.ComInterop` 1.29.380 veya daha yeni kararlı sürüm kullanılır, önizleme sürümü kullanılmaz.
- Program normal kullanıcı olarak çalışır, `app.manifest` değeri `asInvoker`.
- Kayıt yeri `%LocalAppData%\KolayKur`: `settings.json`, `logs\`, `downloads\`.
- Varsayılan dil Türkçe, ikinci dil İngilizcedir. Kullanıcıya görünen her metin `Strings.resx` dosyalarından gelir.
- Onarım dosyaları yalnızca `microsoft/winget-cli` deposunun son sürümünden alınır.
- Paket kimlikleri `StringComparison.OrdinalIgnoreCase` ile karşılaştırılır. Sayı ve tarih yazımında `CultureInfo.InvariantCulture` kullanılır.
- Her görevin sonunda commit atılır ve GitHub'a gönderilir.

## Dikkat edilecekler

Tasarımın açıkça söylemediği bu beş durumun testi, belirtilen görevde yazılır.

1. **Aynı düğmeye iki kez basmak:** Aynı paket için ikinci iş eklenmez (Görev 5).
2. **Türkçe Windows:** `tr-TR` kültüründe paket kimliği karşılaştırmaları bozulmaz (Görev 5).
3. **Kullanıcı adında boşluk ve Türkçe harf:** İndirme ve günlük yolları çalışır (Görev 3).
4. **İş sürerken pencereyi kapatmak:** Program onay ister (Görev 6).
5. **Takılan `winget --version`:** 15 saniye sonra başarısız sayılır, program donmaz (Görev 2).

## Dosya yapısı

```text
src/KolayKur.Core/          İş kodu ve ViewModel'ler, WPF'e bağlı değil
  Models/Package.cs         PackageInfo, OperationKind, OperationStatus, OperationOutcome, OperationProgress
  Winget/                   IWingetClient, ComWingetClient, WingetComFactory
  Health/                   WingetHealthService, ISystemProbe, WindowsSystemProbe, ProcessRunner
  Repair/                   WingetRepairService, IRepairActions, WindowsRepairActions, DownloadVerifier
  Queue/                    OperationQueue, OperationItem
  Lists/                    PackageListFile
  Support/                  AppPaths, FileLog, AppSettings, IDialogService
  Localization/             Strings.resx, Strings.en.resx, Loc
  ViewModels/               Shell, Repair, Search, Updates, Installed, Settings, OperationList
src/KolayKur.App/           WPF: App.xaml, MainWindow.xaml, Views/, WpfDialogService, app.manifest
tools/Probe/                Konsol denemeleri: search, diagnose, repair, install
tests/KolayKur.Core.Tests/  xUnit testleri ve Fakes/
```

## Görev 1: Proje iskeleti ve COM denemesi

**Dosyalar:** `.gitignore`, çözüm ve dört proje (dosya yapısına göre), `src/KolayKur.Core/Winget/WingetComFactory.cs`, `tools/Probe/Program.cs`

**Üretir:** `WingetComFactory` sınıfı: `CreatePackageManager()`, `CreateFindPackagesOptions()`, `CreateCompositeOptions()`, `CreateInstallOptions()`, `CreateUninstallOptions()`, `CreateMatchFilter()`

- [ ] **Araçlar:** Tasarım sekmesindeki "Format sonrası başlangıç" tablosundaki yazılımları kur. Beklenen: `git --version` ve `gh --version` çalışır, `dotnet --version` `10.0.` ile başlar.
- [ ] **Depo:** Proje sahibi `gh auth login` ile giriş yapar. `KolayKur` adıyla özel depo aç, `main` dalını gönder.
- [ ] **Çözüm:** Dosya yapısındaki projeleri oluştur, `Microsoft.WindowsPackageManager.ComInterop` paketini Core'a ekle. Beklenen: `dotnet build` hatasız biter.
- [ ] **COM:** `WingetComFactory` yöntemlerini yaz. Nesneler dış süreçte (`CLSCTX_LOCAL_SERVER`) oluşturulur. CLSID'ler ve örnek için winget-cli deposundaki `src/Microsoft.Management.Deployment.Projection/ClassesDefinition.cs` ve `Initializers/LocalServerInstanceInitializer.cs` dosyalarına bak.
- [ ] **Probe:** `Probe search <metin>` komutunu yaz. `winget` kataloğunda arar, ilk 5 sonucu `Id | Ad | Sürüm` biçiminde yazar.
- [ ] **Tek dosya denemesi:** `dotnet publish tools/Probe -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/probe` ve ardından `out/probe/Probe.exe search git` çalıştır. Beklenen: Çıktıda `Git.Git` satırı görünür.
- [ ] **Karar:** Deneme geçerse Görev 4 `ComWingetClient` yazar. Geçmezse hatayı Tasarım sekmesine işle, Görev 4 yedek yolu izler.
- [ ] **Commit:** `chore: proje iskeleti ve COM denemesi`

## Görev 2: Winget teşhisi

**Dosyalar:** `src/KolayKur.Core/Health/` altındaki dört dosya, `tests/KolayKur.Core.Tests/Health/WingetHealthServiceTests.cs`, `tests/KolayKur.Core.Tests/Fakes/FakeSystemProbe.cs`

**Üretir:**

```csharp
public enum WingetState { Healthy, NotInstalled, DependencyBroken, RegistrationBroken }
public sealed record WingetDiagnosis(WingetState State, string? BrokenDependency, string Detail);
public sealed record ProbeResult(bool Succeeded, string Output, string? Error);
public sealed record PackageDependency(string Name, Version MinVersion);

public interface ISystemProbe
{
    Task<ProbeResult> RunWingetVersionAsync(TimeSpan timeout, CancellationToken ct);
    Version? GetAppInstallerVersion();
    IReadOnlyList<PackageDependency> GetAppInstallerDependencies();
    Version? GetRegisteredVersion(string packageName);
}

public sealed class WingetHealthService(ISystemProbe probe)
{
    public Task<WingetDiagnosis> DiagnoseAsync(CancellationToken ct);
}

public static class ProcessRunner
{
    public static Task<ProbeResult> RunAsync(string file, string arguments, TimeSpan timeout, CancellationToken ct);
}
```

- [ ] **Testleri yaz:** Aşağıdaki tablodaki testler `FakeSystemProbe` ile yazılır, son satır gerçek süreç kullanır.

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `Healthy_when_winget_runs` | Winget başarılı, çıktı v1.29.380 | `State == Healthy`, Detail == "v1.29.380" |
| `NotInstalled_when_app_installer_missing` | Winget başarısız, App Installer yok | `State == NotInstalled` |
| `Paused_framework_is_dependency_broken` | Hata "Sistem dosyaya erişemiyor", App Installer 1.29.379.0, `Microsoft.WindowsAppRuntime.1.8` en az 8000.616.304.0 gerekli ve kayıtlı değil | `State == DependencyBroken`, `BrokenDependency == "Microsoft.WindowsAppRuntime.1.8"` |
| `Old_framework_is_dependency_broken` | Kayıtlı sürüm gerekenden düşük | `State == DependencyBroken` |
| `RegistrationBroken_when_dependencies_ok` | Winget başarısız, bağımlılıklar tamam | `State == RegistrationBroken` |
| `Hung_process_times_out` | `ProcessRunner.RunAsync("ping", "-n 30 127.0.0.1", 1 sn, ct)` | 5 sn içinde döner, `Succeeded == false`, `Error == "timeout"` |

- [ ] **Çalıştır:** `dotnet test --filter "FullyQualifiedName~Health"`. Beklenen: Tipler olmadığı için başarısız olur.
- [ ] **Teşhisi yaz:** `DiagnoseAsync` sırayla bakar: winget çalışıyorsa `Healthy`, App Installer yoksa `NotInstalled`, bağımlılık eksik veya eskiyse `DependencyBroken`, hiçbiri değilse `RegistrationBroken`. Deneme süresi `TimeSpan.FromSeconds(15)` olur.
- [ ] **Gerçek okumayı yaz:** `WindowsSystemProbe` paketleri `Windows.Management.Deployment.PackageManager.FindPackagesForUserWithPackageTypes` ile okur. Bağımlılıkları paket klasöründeki `AppxManifest.xml` dosyasından alır, çünkü arızalı bilgisayarda `Dependencies` değeri boş geldi.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer.
- [ ] **Probe:** `Probe diagnose` komutunu ekle, `State` ve `Detail` yazar. Beklenen: Çalışan bilgisayarda `Healthy`.
- [ ] **Commit:** `feat: winget teşhisi`

## Görev 3: Winget kurulumu ve onarımı

**Dosyalar:** `src/KolayKur.Core/Repair/` altındaki dört dosya, `Support/AppPaths.cs`, `Support/FileLog.cs`, testleri `tests/KolayKur.Core.Tests/Repair/` ve `Support/` altında, `Fakes/FakeRepairActions.cs`

**Kullanır:** `WingetHealthService`, `WingetDiagnosis`

**Üretir:**

```csharp
public sealed record RepairFile(string Name, Uri Url, long Size, string Sha256);
public sealed record WingetRelease(string Tag, RepairFile AppInstaller, RepairFile Dependencies);

public interface IRepairActions
{
    Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct);
    Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct);
    IReadOnlyList<string> ExtractDependencies(string zipPath);
    Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct);
    Task RegisterAppInstallerAsync(CancellationToken ct);
}

public sealed class WingetRepairService(IRepairActions actions, WingetHealthService health, FileLog log)
{
    public Task<WingetDiagnosis> RepairAsync(WingetDiagnosis diagnosis, IProgress<string> progress, CancellationToken ct);
}

public static class DownloadVerifier
{
    public static void Verify(string path, long expectedSize, string expectedSha256);
}

public sealed class DownloadVerificationException(string message) : Exception(message);

public static class AppPaths
{
    public static string Root { get; }
    public static string Logs { get; }
    public static string Downloads { get; }
    public static string SettingsFile { get; }
}

public sealed class FileLog(string directory)
{
    public string CurrentFile { get; }
    public void Info(string message);
    public void Error(string message, Exception? ex = null);
}
```

- [ ] **Testleri yaz:** Onarım testleri `FakeRepairActions` ile çağrı sırasını doğrular.

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `NotInstalled_installs_dependencies_then_app_installer` | `NotInstalled` | Sıra: `Release, Download:Dependencies, Download:AppInstaller, Extract, Add:AppInstaller` |
| `DependencyBroken_adds_only_that_framework_then_registers` | `DependencyBroken`, `Microsoft.WindowsAppRuntime.1.8` | Sıra: `Release, Download:Dependencies, Extract, Add:Microsoft.WindowsAppRuntime.1.8, Register` |
| `RegistrationBroken_only_registers` | `RegistrationBroken` | Sıra: `Register` |
| `Repair_returns_fresh_diagnosis` | Onarımdan sonra winget çalışıyor | Dönen `State == Healthy` |
| `Verify_deletes_truncated_file` | 10 baytlık dosya, beklenen 20 | `DownloadVerificationException`, dosya silinmiş |
| `Verify_deletes_wrong_hash` | Boyut doğru, özet yanlış | `DownloadVerificationException`, dosya silinmiş |
| `Verify_works_with_spaces_and_turkish_letters` | Yol `A B C\İndirilenler\dosya.zip`, boyut ve özet doğru | Hata yok, dosya duruyor |
| `Log_writes_under_path_with_spaces` | Klasör `A B C\Günlük` | `CurrentFile` metni içerir |

- [ ] **Çalıştır:** `dotnet test --filter "FullyQualifiedName~Repair|FullyQualifiedName~Support"`. Beklenen: Başarısız olur.
- [ ] **Onarım akışını yaz:** `RepairAsync` tablodaki sırayı izler ve sonunda `health.DiagnoseAsync` sonucunu döner. `DependencyBroken` için yalnızca dosya adı `<BrokenDependency>_` ile başlayan paketi ekler.
- [ ] **Gerçek eylemleri yaz:** `WindowsRepairActions` sürüm bilgisini `https://api.github.com/repos/microsoft/winget-cli/releases/latest` adresinden `User-Agent: KolayKur` ile okur. Boyutu `size`, özeti `digest` alanından alır (`sha256:` öneki atılır). Dosyayı `AppPaths.Downloads` altına indirir, `DownloadVerifier.Verify` çağırır. Zip'ten `x64` klasörünü çıkarır. Paketleri `PackageManager.AddPackageAsync` ile ekler, `RegisterPackageByFamilyNameAsync` ile `Microsoft.DesktopAppInstaller_8wekyb3d8bbwe` paketini kaydeder.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer.
- [ ] **Probe:** `Probe repair` komutunu ekle, teşhis yapıp onarır ve sonucu yazar.
- [ ] **Windows Sandbox'ta dene:** `out/probe` klasörünü Sandbox'a kopyala. Sırayla `Probe.exe diagnose`, `Probe.exe repair`, `Probe.exe diagnose`, `Probe.exe search git` çalıştır. Beklenen: İlk teşhis `NotInstalled`, ikincisi `Healthy`, aramada `Git.Git` görünür. Yönetici izni istenip istenmediğini not et.
- [ ] **Commit:** `feat: winget kurulumu ve onarımı`

## Görev 4: Winget bağlantısı

**Dosyalar:** `src/KolayKur.Core/Models/Package.cs`, `Winget/IWingetClient.cs`, `Winget/ComWingetClient.cs`, `tests/KolayKur.Core.Tests/Winget/ComWingetClientRealTests.cs`, `tests/KolayKur.Core.Tests/Fakes/FakeWingetClient.cs`

**Kullanır:** `WingetComFactory`

**Üretir:**

```csharp
public sealed record PackageInfo(string Id, string Name, string Publisher, string? InstalledVersion, string? AvailableVersion);
public enum OperationKind { Install, Upgrade, Uninstall }
public enum OperationStatus { Queued, Running, Succeeded, Failed, Cancelled, NotFound }
public sealed record OperationOutcome(OperationStatus Status, string? Message);
public sealed record OperationProgress(double? Percent, string Stage);

public interface IWingetClient
{
    Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct);
    Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct);
    Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct);
    Task<OperationOutcome> InstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);
    Task<OperationOutcome> UpgradeAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);
    Task<OperationOutcome> UninstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);
}

public sealed class CatalogUnavailableException(string message) : Exception(message);
```

- [ ] **Gerçek testleri yaz:** Hepsi `[Trait("Category", "Real")]` taşır ve gerçek winget kullanır.

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `Search_finds_git` | `SearchAsync("git")` | Sonuçlarda `Id == "Git.Git"` |
| `Installed_is_not_empty` | `GetInstalledAsync` | En az bir sonuç, her birinde `InstalledVersion` dolu |
| `Upgrades_have_available_version` | `GetUpgradesAsync` | Her sonuçta `AvailableVersion` dolu |
| `Install_then_uninstall_7zip` | `InstallAsync("7zip.7zip")`, ardından `UninstallAsync` | İkisi de `Succeeded`, ilerleme en az bir kez gelir |
| `Unknown_id_is_not_found` | `InstallAsync("KolayKur.YokBoylePaket")` | `Status == NotFound` |

- [ ] **Çalıştır:** `dotnet test --filter Category=Real`. Beklenen: Başarısız olur.
- [ ] **`ComWingetClient` yaz:** Arama, `winget` kataloğu ile yüklü kataloğu birleştirir, `PackageMatchField.CatalogDefault` ve `ContainsCaseInsensitive` kullanır, en çok 50 sonuç döner. Güncellemeler `IsUpdateAvailable` ile süzülür. Kurma, kimlikle tam eşleşme bulur, bulamazsa `NotFound` döner. Seçenekler `PackageInstallMode.Silent` ve `AcceptPackageAgreements = true`. Kataloğa bağlanılamazsa `CatalogUnavailableException` atılır.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer. İzin penceresi çıkarsa onayla.
- [ ] **İzin reddini dene:** `Probe install 7zip.7zip` komutunu ekle. İzin penceresine "Hayır" de. Beklenen: `Cancelled` yazar. Winget'in bu durumda döndürdüğü kodu `ComWingetClient` içinde `Cancelled` olarak eşle.
- [ ] **`FakeWingetClient` yaz:** Sonraki görevler için paket başına sonuç ve bekletme ayarlanabilir sahte istemci.
- [ ] **Commit:** `feat: winget bağlantısı`

**Yedek yol:** Görev 1'deki deneme başarısızsa aynı arayüzü `CliWingetClient` yazar. `winget` komutunu `--disable-interactivity --accept-source-agreements` ile çalıştırır, tabloyu başlık satırındaki sütun konumlarına göre okur, aynı testleri geçer.

## Görev 5: İşlem sırası

**Dosyalar:** `src/KolayKur.Core/Queue/OperationQueue.cs`, `Queue/OperationItem.cs`, `tests/KolayKur.Core.Tests/Queue/OperationQueueTests.cs`

**Kullanır:** `IWingetClient`, `FileLog`, `FakeWingetClient`

**Üretir:**

```csharp
public sealed partial class OperationItem : ObservableObject
{
    public OperationKind Kind { get; }
    public PackageInfo Package { get; }
    public OperationStatus Status { get; }
    public double? Percent { get; }
    public string? Message { get; }
}

public sealed record BatchSummary(int Succeeded, int Failed, int Cancelled, int NotFound);

public sealed class OperationQueue(IWingetClient client, FileLog log)
{
    public ReadOnlyObservableCollection<OperationItem> Items { get; }
    public bool IsBusy { get; }
    public OperationItem? Enqueue(OperationKind kind, PackageInfo package);
    public event EventHandler<BatchSummary>? BatchCompleted;
    public Task WhenIdleAsync();
}
```

- [ ] **Testleri yaz:**

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `Runs_one_at_a_time_in_order` | 3 iş | Sıra korunur, aynı anda en çok 1 iş çalışır |
| `Failed_item_does_not_stop_next` | 2. iş `Failed` | 3. iş `Succeeded` olur |
| `Denied_elevation_cancels_only_that_item` | 1. iş `Cancelled` | Sonraki iş çalışır |
| `Summary_counts_results` | 2 başarılı, 1 başarısız | `BatchCompleted` değeri `BatchSummary(2, 1, 0, 0)` |
| `Second_click_is_ignored` | Aynı paket, iş bitmeden iki `Enqueue` | İkincisi `null`, kuyrukta tek iş |
| `Id_compare_ignores_case_in_turkish_culture` | `CurrentCulture` `tr-TR`, `INTEL.Driver` ve `intel.driver` | İkincisi `null` |
| `Finished_package_can_run_again` | İş bitti, aynı paket tekrar eklendi | Yeni iş eklenir |

- [ ] **Çalıştır:** `dotnet test --filter "FullyQualifiedName~Queue"`. Beklenen: Başarısız olur.
- [ ] **Kuyruğu yaz:** İşler tek arka plan döngüsünde sırayla çalışır. Kuyruk boşalinca `BatchCompleted` tetiklenir. `Items` değişiklikleri kurucuda yakalanan `SynchronizationContext` üzerinde yapılır. Her sonuç günlüğe yazılır.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer.
- [ ] **Commit:** `feat: işlem sırası`

## Görev 6: Uygulama kabuğu ve açılış kontrolü

**Dosyalar:** `Support/AppSettings.cs`, `Support/IDialogService.cs`, `Localization/` altındaki üç dosya, `ViewModels/ShellViewModel.cs`, `ViewModels/RepairViewModel.cs`, `src/KolayKur.App/` altında `App.xaml`, `MainWindow.xaml`, `Views/RepairPage.xaml`, `WpfDialogService.cs`, `app.manifest`, testleri `tests/KolayKur.Core.Tests/` altında

**Kullanır:** `WingetHealthService`, `WingetRepairService`, `OperationQueue`, `AppPaths`

**Üretir:**

```csharp
public sealed record AppSettings(string Language = "tr", bool LicenseNoticeShown = false)
{
    public static AppSettings Load(string path);
    public void Save(string path);
}

public interface IDialogService
{
    bool Confirm(string message);
    void Inform(string message);
    string? PickOpenFile();
    string? PickSaveFile();
    void OpenFile(string path);
}

public sealed class Loc : ObservableObject
{
    public static Loc Instance { get; }
    public string this[string key] { get; }
    public void SetLanguage(string code);
}

public enum ShellPage { Repair, Search, Updates, Installed, Settings }

public sealed partial class ShellViewModel(WingetHealthService health, OperationQueue queue, IDialogService dialogs, string settingsPath) : ObservableObject
{
    public ShellPage CurrentPage { get; set; }
    public WingetDiagnosis? Diagnosis { get; }
    public Task StartAsync(CancellationToken ct);
    public bool ConfirmClose();
}

public sealed partial class RepairViewModel(WingetRepairService repair, WingetDiagnosis diagnosis) : ObservableObject
{
    public string? StatusText { get; }
    public string? ErrorKey { get; }
    public IAsyncRelayCommand RepairCommand { get; }
    public event EventHandler? Repaired;
}
```

- [ ] **Testleri yaz:**

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `Start_opens_search_when_healthy` | Teşhis `Healthy` | `CurrentPage == Search` |
| `Start_opens_repair_when_broken` | Teşhis `NotInstalled` | `CurrentPage == Repair` |
| `Repair_success_raises_repaired` | Onarım `Healthy` döner | `Repaired` tetiklenir |
| `Repair_failure_shows_error` | Onarım `DependencyBroken` döner | `ErrorKey == "Repair_Failed"` |
| `Close_while_busy_asks_first` | `IsBusy == true`, `Confirm` hayır | `ConfirmClose() == false` |
| `Close_when_idle_does_not_ask` | `IsBusy == false` | `true`, `Confirm` çağrılmadı |
| `License_notice_shows_once` | İki `StartAsync` | `Inform` bir kez çağrılır |
| `Every_key_has_english_text` | `Strings.resx` anahtarları | Her anahtar `Strings.en.resx` içinde var |
| `Language_change_updates_text` | `SetLanguage("en")` | `Loc.Instance["Menu_Search"]` İngilizce döner |
| `Broken_settings_file_gives_defaults` | Bozuk `settings.json` | `Language == "tr"` |

- [ ] **Çalıştır:** `dotnet test --filter "FullyQualifiedName~Shell|FullyQualifiedName~Settings|FullyQualifiedName~Strings"`. Beklenen: Başarısız olur.
- [ ] **Kabuğu yaz:** `StartAsync` ayarları yükler, dili uygular, teşhis yapar ve sayfayı seçer. Lisans notu ilk açılışta `Inform` ile gösterilir ve `LicenseNoticeShown` kaydedilir.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer.
- [ ] **WPF kabuğunu yaz:** Solda dört menü, ortada sayfa, altta işlem listesi alanı. `Closing` olayı `ConfirmClose` çağırır. `app.manifest` değeri `asInvoker`. Metinler `{Binding [Anahtar], Source={x:Static loc:Loc.Instance}}` ile gelir.
- [ ] **Elle dene:** `dotnet run --project src/KolayKur.App`. Beklenen: Program Türkçe açılır, dört menü görünür.
- [ ] **Commit:** `feat: uygulama kabuğu ve açılış kontrolü`

## Görev 7: Ara ve Güncellemeler sayfaları

**Dosyalar:** `ViewModels/SearchViewModel.cs`, `UpdatesViewModel.cs`, `OperationListViewModel.cs`, `src/KolayKur.App/Views/SearchPage.xaml`, `UpdatesPage.xaml`, `OperationListView.xaml`, testleri `tests/KolayKur.Core.Tests/ViewModels/` altında

**Kullanır:** `IWingetClient`, `OperationQueue`, `Loc`, `CatalogUnavailableException`

**Üretir:**

```csharp
public sealed partial class SelectablePackage(PackageInfo package) : ObservableObject
{
    public PackageInfo Package { get; }
    public bool IsSelected { get; set; }
}

public sealed partial class SearchViewModel(IWingetClient client, OperationQueue queue) : ObservableObject
{
    public string Query { get; set; }
    public ObservableCollection<SelectablePackage> Results { get; }
    public string? ErrorKey { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IRelayCommand<PackageInfo> InstallCommand { get; }
    public IRelayCommand InstallSelectedCommand { get; }
}

public sealed partial class UpdatesViewModel(IWingetClient client, OperationQueue queue) : ObservableObject
{
    public ObservableCollection<PackageInfo> Updates { get; }
    public string? ErrorKey { get; }
    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand<PackageInfo> UpgradeCommand { get; }
    public IRelayCommand UpgradeAllCommand { get; }
}

public sealed partial class OperationListViewModel(OperationQueue queue) : ObservableObject
{
    public ReadOnlyObservableCollection<OperationItem> Items { get; }
    public string? SummaryText { get; }
}
```

- [ ] **Testleri yaz:**

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `Empty_query_does_not_call_winget` | `Query` boş | `SearchAsync` çağrılmaz |
| `Newer_search_wins` | Eski arama yenisinden sonra biter | `Results` yeni aramayı gösterir |
| `Install_selected_enqueues_each` | 3 sonuçtan 2'si seçili | Kuyrukta 2 `Install` işi |
| `Offline_search_shows_retry_message` | `CatalogUnavailableException` | `ErrorKey == "Error_NoInternet"` |
| `Upgrade_all_enqueues_every_item` | 3 güncelleme | Kuyrukta 3 `Upgrade` işi |
| `Updates_reload_after_batch` | `BatchCompleted` tetiklendi | `GetUpgradesAsync` yeniden çağrılır |
| `Summary_text_counts_results` | `BatchSummary(2, 1, 0, 0)` | `SummaryText` iki sayıyı da içerir |

- [ ] **Çalıştır:** `dotnet test --filter "FullyQualifiedName~ViewModels"`. Beklenen: Yeni testler başarısız olur.
- [ ] **ViewModel'leri yaz:** Yeni arama eskisini `CancellationToken` ile iptal eder.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer.
- [ ] **Sayfaları yaz:** Ara sayfasında arama kutusu, onay kutulu liste, "Kur" ve "Seçilenleri kur" düğmeleri bulunur. Hata varsa mesaj ve "Tekrar dene" görünür. Güncellemeler sayfası ad, kurulu sürüm ve yeni sürümü gösterir. İşlem listesi durum, ilerleme çubuğu ve özeti gösterir.
- [ ] **Elle dene:** `git` ara, `7zip.7zip` kur, Güncellemeler sayfasını aç. Beklenen: Sonuçlar gelir, ilerleme görünür, iş bitince özet çıkar.
- [ ] **Commit:** `feat: ara ve güncellemeler sayfaları`

## Görev 8: Yüklü uygulamalar, liste dosyası ve Ayarlar

**Dosyalar:** `Lists/PackageListFile.cs`, `ViewModels/InstalledViewModel.cs`, `ViewModels/SettingsViewModel.cs`, `src/KolayKur.App/Views/InstalledPage.xaml`, `SettingsPage.xaml`, testleri `tests/KolayKur.Core.Tests/` altında, `tests/KolayKur.Core.Tests/Samples/winget-export.json`

**Kullanır:** `IWingetClient`, `OperationQueue`, `IDialogService`, `ShellViewModel`, `FileLog`, `Loc`

**Üretir:**

```csharp
public sealed class PackageListFormatException(string message) : Exception(message);

public static class PackageListFile
{
    public static void Save(string path, IEnumerable<PackageInfo> packages, string wingetVersion);
    public static IReadOnlyList<string> Load(string path);
}

public sealed partial class InstalledViewModel(IWingetClient client, OperationQueue queue, IDialogService dialogs, Func<string> wingetVersion) : ObservableObject
{
    public ObservableCollection<PackageInfo> Packages { get; }
    public string? ErrorKey { get; }
    public IAsyncRelayCommand LoadCommand { get; }
    public IRelayCommand<PackageInfo> UninstallCommand { get; }
    public IRelayCommand ExportCommand { get; }
    public IRelayCommand ImportCommand { get; }
}

public sealed partial class SettingsViewModel(ShellViewModel shell, FileLog log, IDialogService dialogs, string settingsPath) : ObservableObject
{
    public string Language { get; set; }
    public WingetDiagnosis? Diagnosis { get; }
    public IRelayCommand OpenRepairCommand { get; }
    public IRelayCommand OpenLogCommand { get; }
}
```

- [ ] **Örnek dosyayı al:** `winget export -o tests/KolayKur.Core.Tests/Samples/winget-export.json` çalıştır. `PackageListFile` bu dosyanın şemasını ve alanlarını birebir izler.
- [ ] **Testleri yaz:**

| Test | Girdi | Doğrulama |
| --- | --- | --- |
| `Save_then_Load_returns_same_ids` | `Git.Git`, `7zip.7zip` | Aynı kimlikler okunur |
| `Load_reads_real_winget_export` | `Samples/winget-export.json` | En az bir kimlik okunur |
| `Load_rejects_broken_file` | Geçersiz JSON | `PackageListFormatException` |
| `Import_of_broken_file_starts_nothing` | Geçersiz JSON | Kuyruk boş, `ErrorKey == "Error_ListFile"` |
| `Import_unknown_package_continues` | `Git.Git`, `Yok.Boyle`, ikincisi `NotFound` döner | İlk iş `Succeeded`, ikinci iş `NotFound` |
| `Uninstall_asks_first` | `Confirm` hayır | Kuyruk boş |
| `Uninstall_enqueues_after_yes` | `Confirm` evet | Kuyrukta bir `Uninstall` işi |
| `Language_change_is_saved_and_applied` | `Language = "en"` | `Loc` İngilizce, `settings.json` değeri `en` |
| `Open_log_opens_current_file` | `OpenLogCommand` | `OpenFile(log.CurrentFile)` çağrılır |

- [ ] **Çalıştır:** `dotnet test --filter "FullyQualifiedName~Lists|FullyQualifiedName~Installed|FullyQualifiedName~SettingsViewModel"`. Beklenen: Başarısız olur.
- [ ] **Kodu yaz:** Listeden kurma her kimlik için `new PackageInfo(id, id, "", null, null)` ile `Install` işi ekler. `wingetVersion`, `Diagnosis.Detail` değerinden `v` öneki atılarak gelir.
- [ ] **Çalıştır:** Aynı komut. Beklenen: Hepsi geçer.
- [ ] **Sayfaları yaz:** Yüklü uygulamalar sayfasında liste, "Kaldır", "Listeyi dışa aktar" ve "Listeden kur" bulunur. Ayarlar sayfasında dil seçimi, winget durumu, "Onar" ve "Günlüğü aç" bulunur.
- [ ] **Elle dene:** Dili İngilizceye çevir, listeyi dışa aktar, `7zip.7zip` paketini kaldır. Beklenen: Metinler anında değişir, dosya oluşur, kaldırmadan önce onay sorulur.
- [ ] **Commit:** `feat: yüklü uygulamalar, liste dosyası ve ayarlar`

## Görev 9: Paketleme ve sürüm

**Dosyalar:** `README.md`

- [ ] **Paketle:** `dotnet publish src/KolayKur.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o out/release`. Beklenen: `out/release/KolayKur.exe` oluşur. Boyutu Tasarım sekmesindeki Dağıtım bölümüne yaz.
- [ ] **Temiz Windows denemesi:** Arama, `7zip.7zip` kurma, güncelleme listesi ve kaldırma denenir. Beklenen: Hepsi terminal açmadan çalışır.
- [ ] **Windows Sandbox denemesi:** `KolayKur.exe` Sandbox'a kopyalanıp açılır. Beklenen: .NET olmadan açılır, onarım ekranı gelir, "Onar" sonrası arama çalışır.
- [ ] **Liste denemesi:** Programın ürettiği dosya `winget import -i <dosya> --ignore-unavailable` ile okunur. Beklenen: Biçim hatası yok.
- [ ] **README yaz:** Programın ne yaptığı, nasıl indirildiği ve Windows uyarısının nasıl geçildiği yazılır.
- [ ] **Yayın izni:** Proje sahibine sor: Depo herkese açık olsun mu? Onay olmadan devam etme.
- [ ] **Sürüm:** Onaydan sonra depoyu herkese açık yap, `gh release create v0.1.0 out/release/KolayKur.exe` ile sürüm oluştur. Beklenen: Sayfadan hesapsız indirilebilir.
- [ ] **Arkadaş denemesi:** Bir arkadaş indirir, uyarıyı geçer ve programı açar.
- [ ] **Commit:** `docs: README ve ilk sürüm`
