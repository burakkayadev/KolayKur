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
    public async Task Close_while_repairing_asks_first()
    {
        // Arrange: onarım ilk adımında bekletiliyor; kuyruk boş ama onarım sürüyor.
        var actions = new GatedRepairActions();
        var repair = new WingetRepairService(actions, new WingetHealthService(NotInstalledProbe), Log);
        var shell = CreateShell(NotInstalledProbe, createRepair: d => new RepairViewModel(repair, d, Log));
        await shell.StartAsync(CancellationToken.None);
        var running = shell.Repair!.RepairCommand.ExecuteAsync(null);
        await actions.Entered.WaitAsync(Timeout);
        dialogs.ConfirmResult = false;

        try
        {
            // Act + Assert: onarım sürerken sorar ve yanıtı döndürür.
            Assert.False(shell.ConfirmClose());
            dialogs.ConfirmResult = true;
            Assert.True(shell.ConfirmClose());
            Assert.Equal(2, dialogs.Confirmed.Count);
        }
        finally
        {
            actions.Release();
            await running.WaitAsync(Timeout);
        }

        // Onarım bitti: artık sormaz.
        dialogs.Confirmed.Clear();
        Assert.True(shell.ConfirmClose());
        Assert.Empty(dialogs.Confirmed);
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
    public async Task License_notice_shows_for_new_users()
    {
        await CreateShell(new FakeSystemProbe()).StartAsync(CancellationToken.None);

        Assert.Single(dialogs.Informed);
        Assert.Equal(ShellViewModel.CurrentLicenseNoticeVersion, AppSettings.Load(SettingsPath).LicenseNoticeVersion);
    }

    [Fact]
    public async Task Updated_notice_shows_once_for_old_users()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{\"LicenseNoticeShown\":true}");

        await CreateShell(new FakeSystemProbe()).StartAsync(CancellationToken.None);
        await CreateShell(new FakeSystemProbe()).StartAsync(CancellationToken.None);

        Assert.Single(dialogs.Informed);
        var saved = AppSettings.Load(SettingsPath);
        Assert.Equal(2, saved.LicenseNoticeVersion);
        Assert.True(saved.LicenseNoticeShown);
    }

    [Fact]
    public async Task Current_notice_version_is_not_shown_again()
    {
        new AppSettings(LicenseNoticeShown: true, LicenseNoticeVersion: 2).Save(SettingsPath);

        await CreateShell(new FakeSystemProbe()).StartAsync(CancellationToken.None);

        Assert.Empty(dialogs.Informed);
    }

    [Fact]
    public void Notice_mentions_store_in_both_languages()
    {
        try
        {
            foreach (var language in new[] { "tr", "en" })
            {
                Loc.Instance.SetLanguage(language);
                Assert.Contains("Microsoft Store", Loc.Instance["License_Notice"]);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage("tr");
        }
    }

    [Fact]
    public async Task Start_applies_saved_language_and_keeps_it_when_saving_notice()
    {
        new AppSettings("en").Save(SettingsPath);
        var shell = CreateShell(new FakeSystemProbe());

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal("Search", Loc.Instance["Menu_Search"]);
        Assert.Equal(new AppSettings("en", LicenseNoticeShown: true, LicenseNoticeVersion: 2), AppSettings.Load(SettingsPath));
    }

    [Fact]
    public async Task Repair_page_is_never_shown_without_a_broken_diagnosis()
    {
        // Diagnosis yok (örn. açılış denetimi istisna fırlattı).
        var failing = new ControllableSystemProbe { Failure = new InvalidOperationException("AppX bozuk") };
        var shell = CreateShell(failing);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.StartAsync(CancellationToken.None));

        shell.CurrentPage = ShellPage.Repair;

        Assert.Equal(ShellPage.Search, shell.CurrentPage);

        // Diagnosis sağlıklı.
        var healthy = CreateShell(new FakeSystemProbe());
        await healthy.StartAsync(CancellationToken.None);

        healthy.CurrentPage = ShellPage.Repair;

        Assert.Equal(ShellPage.Search, healthy.CurrentPage);
    }

    [Fact]
    public async Task Recheck_updates_the_diagnosis_without_changing_the_page()
    {
        var probe = new ControllableSystemProbe { Working = true };
        var shell = CreateShell(probe);
        await shell.StartAsync(CancellationToken.None);
        shell.CurrentPage = ShellPage.Settings;
        probe.Working = false;

        var result = await shell.RecheckAsync(CancellationToken.None);

        Assert.Equal(WingetState.NotInstalled, result.State);
        Assert.Same(result, shell.Diagnosis);
        Assert.Equal(ShellPage.Settings, shell.CurrentPage);
        Assert.False(shell.IsChecking);
    }

    [Fact]
    public async Task Recheck_failure_reaches_the_caller_and_clears_the_checking_flag()
    {
        var probe = new ControllableSystemProbe { Working = true };
        var shell = CreateShell(probe);
        await shell.StartAsync(CancellationToken.None);
        probe.Failure = new InvalidOperationException("AppX bozuk");

        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.RecheckAsync(CancellationToken.None));

        Assert.False(shell.IsChecking);
        Assert.Equal(WingetState.Healthy, shell.Diagnosis?.State);
    }

    [Fact]
    public async Task Became_healthy_raised_once_on_healthy_start()
    {
        var shell = CreateShell(new FakeSystemProbe());
        var raised = 0;
        shell.BecameHealthy += (_, _) => raised++;

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Became_healthy_not_raised_when_start_is_broken()
    {
        var shell = CreateShell(NotInstalledProbe);
        var raised = 0;
        shell.BecameHealthy += (_, _) => raised++;

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task Became_healthy_raised_after_successful_repair()
    {
        var probe = new TogglingProbe();
        var repair = new WingetRepairService(new FakeRepairActions(), new WingetHealthService(probe), Log);
        var shell = CreateShell(probe, createRepair: d => new RepairViewModel(repair, d, Log));
        var raised = 0;
        shell.BecameHealthy += (_, _) => raised++;
        await shell.StartAsync(CancellationToken.None);
        Assert.Equal(0, raised);

        probe.Working = true;
        await shell.Repair!.RepairCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => shell.CurrentPage == ShellPage.Search);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Healthy_recheck_does_not_raise_again()
    {
        var shell = CreateShell(new FakeSystemProbe());
        var raised = 0;
        shell.BecameHealthy += (_, _) => raised++;
        await shell.StartAsync(CancellationToken.None);

        await shell.RecheckAsync(CancellationToken.None);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Became_healthy_handler_failure_does_not_break_diagnosis()
    {
        var shell = CreateShell(new FakeSystemProbe());
        shell.BecameHealthy += (_, _) => throw new InvalidOperationException("dinleyici patladı");

        await shell.StartAsync(CancellationToken.None);

        Assert.Equal(WingetState.Healthy, shell.Diagnosis?.State);
        Assert.False(shell.IsChecking);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(Timeout);
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }

    /// <summary>İlk adımı (sürüm sorgusu) <see cref="Release"/> çağrılana kadar bekleten onarım eylemleri.</summary>
    private sealed class GatedRepairActions : IRepairActions
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly FakeRepairActions inner = new();

        public Task Entered => entered.Task;

        public void Release() => released.TrySetResult();

        public async Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct)
        {
            entered.TrySetResult();
            await released.Task.WaitAsync(ct);
            return await inner.GetLatestReleaseAsync(ct);
        }

        public Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct) =>
            inner.DownloadAsync(file, progress, ct);

        public IReadOnlyList<string> ExtractDependencies(string zipPath) => inner.ExtractDependencies(zipPath);

        public Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct) =>
            inner.AddPackageAsync(packagePath, dependencyPaths, ct);

        public Task RegisterAppInstallerAsync(CancellationToken ct) => inner.RegisterAppInstallerAsync(ct);
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
