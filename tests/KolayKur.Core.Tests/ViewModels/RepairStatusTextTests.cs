using System.Globalization;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;

namespace KolayKur.Core.Tests.ViewModels;

// Durum metni Loc.Instance'tan geldiği için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class RepairStatusTextTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Loc.Instance.SetLanguage("tr");
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public void Every_repair_stage_has_text_in_both_languages()
    {
        foreach (var stage in Enum.GetValues<RepairStage>())
        {
            var key = RepairViewModel.StageKey(stage);
            foreach (var code in new[] { "tr", "en" })
            {
                var text = Loc.Resources.GetString(key, CultureInfo.GetCultureInfo(code));
                Assert.False(string.IsNullOrWhiteSpace(text), $"{code}: {key}");
            }
        }
    }

    [Fact]
    public async Task Status_text_follows_the_language()
    {
        // Arrange: onarım ilk adımda ("son sürüm aranıyor") bekliyor.
        Loc.Instance.SetLanguage("tr");
        var actions = new HoldingActions();
        var log = new FileLog(logDirectory);
        var service = new WingetRepairService(actions, new WingetHealthService(new FakeSystemProbe()), log);
        var viewModel = new RepairViewModel(
            service, new WingetDiagnosis(WingetState.NotInstalled, null, "yok"), log);
        Assert.Null(viewModel.StatusText);

        var running = viewModel.RepairCommand.ExecuteAsync(null);
        await actions.Entered.WaitAsync(Timeout);
        await WaitUntilAsync(() => viewModel.StatusText is not null);
        Assert.Equal("Son winget sürümü aranıyor.", viewModel.StatusText);

        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        try
        {
            // Act
            Loc.Instance.SetLanguage("en");

            // Assert: aynı adım, yeni dilde ve bildirimle.
            Assert.Equal("Looking for the latest winget release.", viewModel.StatusText);
            Assert.Contains(nameof(RepairViewModel.StatusText), changed);
        }
        finally
        {
            actions.Release();
            await running.WaitAsync(Timeout);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Koşul zamanında gerçekleşmedi.");
            await Task.Delay(10);
        }
    }

    /// <summary>İlk adımda (sürüm arama) serbest bırakılana kadar bekleyen onarım eylemleri.</summary>
    private sealed class HoldingActions : IRepairActions
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
}
