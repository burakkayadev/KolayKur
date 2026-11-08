using System.Diagnostics;
using System.Windows;
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
        MainWindow? window = null;
        try
        {
            var app = Compose(dialogs);
            window = new MainWindow(app.Shell, app.Search, app.Updates, app.Installed, app.Settings, app.Operations);
            MainWindow = window;
            window.Show();

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
    /// Nesnelerin tek kurulum yeri. <see cref="OperationQueue"/> ve ona abone olan ViewModel'ler arayüz iş
    /// parçacığında kurulmalıdır (kuyruk olaylarını kurulduğu bağlamda tetikler).
    /// </summary>
    private Composition Compose(IDialogService dialogs)
    {
        var fileLog = log = new FileLog(AppPaths.Logs);
        var health = new WingetHealthService(new WindowsSystemProbe());
        var repair = new WingetRepairService(new WindowsRepairActions(), health, fileLog);
        var client = new ComWingetClient(new WingetComFactory());
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
            new SearchViewModel(client, queue),
            new UpdatesViewModel(client, queue),
            new InstalledViewModel(client, queue, dialogs, () => shell.WingetVersion),
            new SettingsViewModel(shell, fileLog, dialogs, AppPaths.SettingsFile),
            new OperationListViewModel(queue));
    }

    private sealed record Composition(
        ShellViewModel Shell,
        SearchViewModel Search,
        UpdatesViewModel Updates,
        InstalledViewModel Installed,
        SettingsViewModel Settings,
        OperationListViewModel Operations);
}
