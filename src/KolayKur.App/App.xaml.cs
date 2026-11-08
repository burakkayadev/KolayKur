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
        try
        {
            var shell = Compose(dialogs);
            var window = new MainWindow(shell);
            MainWindow = window;
            window.Show();

            await shell.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            log?.Error("Program açılırken beklenmeyen hata.", ex);
            dialogs.Inform(Loc.Instance["Startup_Failed"]);
        }
    }

    /// <summary>Nesnelerin tek kurulum yeri. <see cref="OperationQueue"/> arayüz iş parçacığında kurulmalıdır.</summary>
    private ShellViewModel Compose(IDialogService dialogs)
    {
        var fileLog = log = new FileLog(AppPaths.Logs);
        var health = new WingetHealthService(new WindowsSystemProbe());
        var repair = new WingetRepairService(new WindowsRepairActions(), health, fileLog);
        var queue = new OperationQueue(new ComWingetClient(new WingetComFactory()), fileLog);

        return new ShellViewModel(
            health,
            queue,
            dialogs,
            AppPaths.SettingsFile,
            diagnosis => new RepairViewModel(repair, diagnosis, fileLog),
            fileLog);
    }
}
