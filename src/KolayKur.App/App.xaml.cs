using System.Diagnostics;
using System.Windows;
using KolayKur.App.Icons;
using KolayKur.Core.Icons;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Queue;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;
using KolayKur.Core.ViewModels;
using KolayKur.Core.Winget;

namespace KolayKur.App;

public partial class App : Application
{
    private FileLog? log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var dialogs = new WpfDialogService();
        var fileLog = log = new FileLog(AppPaths.Logs);
        HookGlobalErrorHandlers(new UnhandledErrorReporter(fileLog, dialogs));

        // Kullanıcıya görünen ilk şeyden (klasör yazılamıyor uyarısı, ilk pencere) önce kayıtlı dil uygulanır.
        var saved = AppSettings.Load(AppPaths.SettingsFile);
        Loc.Instance.SetLanguage(saved.Language);

        MainWindow? window = null;
        try
        {
            // Kayıtlı tema, pencere oluşmadan önce uygulanır: açılışta yanlış temada parlama olmaz. Hata olursa "Startup_Failed" yoluna düşer.
            var themes = new WpfUiThemeService();
            themes.Apply(saved.Theme);

            // COM'a (winget nesnelerine) ilk dokunuştan önce winmd programın yanında olmalı.
            if (!EnsureWinmd(dialogs))
            {
                Shutdown(1);
                return;
            }

            var app = Compose(dialogs, themes);
            window = new MainWindow(app.Shell, app.Search, app.Updates, app.Installed, app.Settings, app.Operations);
            themes.Attach(window);
            MainWindow = window;
            window.Show();

            // Winget sağlıklı olunca güncelleme listesi arka planda yüklenir (menü rozeti için); hata iletişim kutusu açmaz,
            // LoadAsync hatayı kendi içinde ErrorKey ve günlüğe işler.
            app.Shell.BecameHealthy += (_, _) => _ = app.Updates.LoadCommand.ExecuteAsync(null);

            await app.Shell.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Günlük de açılamamış olabilir; hata her durumda izleme çıktısına da gider ve asla yutulmaz.
            Trace.TraceError($"KolayKur açılırken beklenmeyen hata: {ex}");
            log.TryError("Program açılırken beklenmeyen hata.", ex);
            dialogs.Inform(Loc.Instance["Startup_Failed"]);

            // Pencere hiç açılamadıysa görünmez bir süreç kalmasın; açıksa kullanıcıya bırakılır.
            if (window is null || !window.IsVisible)
            {
                Shutdown(1);
            }
        }
    }

    /// <summary>
    /// Yakalanmamış hataların son durağı. Arayüz iş parçacığındaki hata günlüğe yazılır, kullanıcıya genel bir mesaj
    /// gösterilir ve program çalışmaya devam eder (bellek tükenmesi hariç); arka plan hataları yalnızca günlüğe yazılır.
    /// </summary>
    private void HookGlobalErrorHandlers(UnhandledErrorReporter reporter)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            reporter.ReportAndInform("arayüz iş parçacığı", args.Exception);
            args.Handled = args.Exception is not OutOfMemoryException;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            reporter.ReportOnly("gözlemlenmemiş görev hatası", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            reporter.ReportOnly(args.IsTerminating ? "program sonlanıyor" : "AppDomain", args.ExceptionObject);
    }

    /// <summary>
    /// Tek exe dağıtımında winmd dosyasını gömülü kaynaktan programın yanına çıkarır. Klasör yazılamazsa kullanıcıya
    /// ne yapacağını söyler ve <c>false</c> döner.
    /// </summary>
    private bool EnsureWinmd(IDialogService dialogs)
    {
        var result = WinmdBootstrapper.EnsureExtracted(
            AppContext.BaseDirectory,
            () => typeof(App).Assembly.GetManifestResourceStream("Microsoft.Management.Deployment.winmd"));

        if (result.Status != WinmdStatus.ExtractionFailed)
        {
            return true;
        }

        Trace.TraceError($"winmd çıkarılamadı ({AppContext.BaseDirectory}): {result.Error}");
        log.TryError($"winmd dosyası {AppContext.BaseDirectory} klasörüne yazılamadı.", result.Error);
        dialogs.Inform(Loc.Instance["Startup_FolderNotWritable"]);
        return false;
    }

    /// <summary>
    /// Nesnelerin tek kurulum yeri. <see cref="OperationQueue"/> ve ona abone olan ViewModel'ler arayüz iş
    /// parçacığında kurulmalıdır (kuyruk olaylarını kurulduğu bağlamda tetikler).
    /// </summary>
    private Composition Compose(IDialogService dialogs, IThemeService themes)
    {
        var fileLog = log ??= new FileLog(AppPaths.Logs);
        var health = new WingetHealthService(new WindowsSystemProbe());
        var repair = new WingetRepairService(new WindowsRepairActions(), health, fileLog);
        var client = new ComWingetClient(new WingetComFactory(), fileLog);
        AppIcons.Service = new IconService(
            client, new HttpIconFetcher(), new WindowsInstalledIconReader(), new WindowsIconDecoder(), new IconCache(AppPaths.Icons), fileLog);
        var queue = new OperationQueue(client, fileLog);

        var shell = new ShellViewModel(
            health,
            queue,
            dialogs,
            AppPaths.SettingsFile,
            diagnosis => new RepairViewModel(repair, diagnosis, fileLog),
            fileLog);

        return new Composition(
            shell,
            new SearchViewModel(client, queue, fileLog),
            new UpdatesViewModel(client, queue, fileLog),
            new InstalledViewModel(client, queue, dialogs, () => shell.WingetVersion, fileLog),
            new SettingsViewModel(shell, fileLog, dialogs, AppPaths.SettingsFile, themes) { AppVersion = ReadAppVersion() },
            new OperationListViewModel(queue));
    }

    // Uygulama derlemesinin sürümü, 3 bölümlü ("1.0.0"); okunamazsa boş (Hakkında yine açılır).
    private static string ReadAppVersion() =>
        typeof(App).Assembly.GetName().Version?.ToString(3) ?? string.Empty;

    private sealed record Composition(
        ShellViewModel Shell,
        SearchViewModel Search,
        UpdatesViewModel Updates,
        InstalledViewModel Installed,
        SettingsViewModel Settings,
        OperationListViewModel Operations);
}
