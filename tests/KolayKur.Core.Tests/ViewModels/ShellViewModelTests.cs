using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.ViewModels;

// StartAsync dili Loc.Instance üzerinde uyguladığı için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class ShellViewModelTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    private readonly FakeDialogService dialogs = new();

    public void Dispose()
    {
        Loc.Instance.SetLanguage("tr");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(directory, "settings.json");

    private FileLog Log => new(Path.Combine(directory, "logs"));

    private static readonly FakeSystemProbe NotInstalledProbe = new()
    {
        WingetResult = new ProbeResult(false, "", "winget yok"),
        AppInstallerVersion = null,
    };

    private OperationQueue CreateQueue(IWingetClient? client = null) => new(client ?? new FakeWingetClient(), Log);

    private ShellViewModel CreateShell(
        ISystemProbe probe, OperationQueue? queue = null, Func<WingetDiagnosis, RepairViewModel>? createRepair = null) =>
        new(new WingetHealthService(probe), queue ?? CreateQueue(), dialogs, SettingsPath, createRepair, Log);

    [Fact]
    public async Task Start_opens_search_when_healthy()
    {
        var shell = CreateShell(new FakeSystemProbe());

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal(ShellPage.Search, shell.CurrentPage);
        Assert.Equal(WingetState.Healthy, shell.Diagnosis?.State);
        Assert.Null(shell.Repair);
        Assert.False(shell.IsChecking);
    }

    [Fact]
    public async Task Start_opens_repair_when_broken()
    {
        var repair = new WingetRepairService(new FakeRepairActions(), new WingetHealthService(NotInstalledProbe), Log);
        var shell = CreateShell(NotInstalledProbe, createRepair: d => new RepairViewModel(repair, d, Log));

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal(ShellPage.Repair, shell.CurrentPage);
        Assert.Equal(WingetState.NotInstalled, shell.Diagnosis?.State);
        Assert.NotNull(shell.Repair);
        Assert.False(shell.IsMenuEnabled);
    }

    [Fact]
    public async Task Repaired_runs_diagnosis_again_and_opens_search()
    {
        var probe = new TogglingProbe();
        var repair = new WingetRepairService(new FakeRepairActions(), new WingetHealthService(probe), Log);
        var shell = CreateShell(probe, createRepair: d => new RepairViewModel(repair, d, Log));
        await shell.StartAsync(CancellationToken.None);
        Assert.Equal(ShellPage.Repair, shell.CurrentPage);

        probe.Working = true;
        await shell.Repair!.RepairCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => shell.CurrentPage == ShellPage.Search);

        Assert.Equal(WingetState.Healthy, shell.Diagnosis?.State);
        Assert.Null(shell.Repair);
        Assert.True(shell.IsMenuEnabled);
    }

    [Fact]
    public async Task Close_while_busy_asks_first()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var queue = CreateQueue(client);
        var shell = CreateShell(new FakeSystemProbe(), queue);
        queue.Enqueue(OperationKind.Install, new PackageInfo("A.One", "A.One", "Yayıncı", null, "1.0"));
        await gate.Started.WaitAsync(Timeout);
        Assert.True(queue.IsBusy);
        dialogs.ConfirmResult = false;

        try
        {
            Assert.False(shell.ConfirmClose());
            Assert.Single(dialogs.Confirmed);

            dialogs.ConfirmResult = true;
            Assert.True(shell.ConfirmClose());
            Assert.Equal(2, dialogs.Confirmed.Count);
        }
        finally
        {
            gate.Release();
            await queue.WhenIdleAsync().WaitAsync(Timeout);
        }
    }

    [Fact]
    public void Close_when_idle_does_not_ask()
    {
        var queue = CreateQueue();
        var shell = CreateShell(new FakeSystemProbe(), queue);
        Assert.False(queue.IsBusy);

        var canClose = shell.ConfirmClose();

        Assert.True(canClose);
        Assert.Empty(dialogs.Confirmed);
    }

    [Fact]
    public async Task License_notice_shows_once()
    {
        var shell = CreateShell(new FakeSystemProbe());

        await shell.StartAsync(CancellationToken.None);
        await shell.StartAsync(CancellationToken.None);
        await CreateShell(new FakeSystemProbe()).StartAsync(CancellationToken.None);

        Assert.Single(dialogs.Informed);
        Assert.Equal(Loc.Instance["License_Notice"], dialogs.Informed[0]);
        Assert.True(AppSettings.Load(SettingsPath).LicenseNoticeShown);
    }

    [Fact]
    public async Task Start_applies_saved_language_and_keeps_it_when_saving_notice()
    {
        new AppSettings("en").Save(SettingsPath);
        var shell = CreateShell(new FakeSystemProbe());

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal("Search", Loc.Instance["Menu_Search"]);
        Assert.Equal(new AppSettings("en", LicenseNoticeShown: true), AppSettings.Load(SettingsPath));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    /// <summary>Çalışıp çalışmadığı testin ortasında değişebilen winget; <see cref="Working"/> true olunca sağlıklıdır.</summary>
    private sealed class TogglingProbe : ISystemProbe
    {
        public volatile bool Working;

        public Task<ProbeResult> RunWingetVersionAsync(TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(Working ? new ProbeResult(true, "v1.29.380", null) : new ProbeResult(false, "", "winget yok"));

        public Version? GetAppInstallerVersion() => null;

        public IReadOnlyList<PackageDependency> GetAppInstallerDependencies() => [];

        public Version? GetRegisteredVersion(string packageName) => null;
    }
}
