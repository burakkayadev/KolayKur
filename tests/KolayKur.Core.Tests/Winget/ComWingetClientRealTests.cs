using KolayKur.Core.Models;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Winget;

/// <summary>Gerçek winget ile çalışır. Kurulum yapan test hariç hepsi salt okunurdur.</summary>
[Trait("Category", "Real")]
public class ComWingetClientRealTests
{
    private const string SevenZipId = "7zip.7zip";
    private const int SearchLimit = 50;
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(2);

    private readonly ComWingetClient client = new(new WingetComFactory());

    private static CancellationToken Timeout() => new CancellationTokenSource(TestTimeout).Token;

    [Fact]
    public void Factory_creates_all_objects()
    {
        var factory = new WingetComFactory();

        Assert.NotNull(factory.CreatePackageManager());
        Assert.NotNull(factory.CreateFindPackagesOptions());
        Assert.NotNull(factory.CreateCompositeOptions());
        Assert.NotNull(factory.CreateInstallOptions());
        Assert.NotNull(factory.CreateUninstallOptions());
        Assert.NotNull(factory.CreateMatchFilter());
    }

    [Fact]
    public async Task Search_finds_git()
    {
        var results = await client.SearchAsync("git", Timeout());

        Assert.Contains(results, p => string.Equals(p.Id, "Git.Git", StringComparison.OrdinalIgnoreCase));
        Assert.True(results.Count <= SearchLimit, $"{results.Count} sonuç döndü, sınır {SearchLimit}.");
    }

    [Fact]
    public async Task Installed_is_not_empty()
    {
        var installed = await client.GetInstalledAsync(Timeout());

        Assert.NotEmpty(installed);
        Assert.All(installed, p => Assert.False(string.IsNullOrWhiteSpace(p.InstalledVersion), p.Id));
    }

    [Fact]
    public async Task Upgrades_have_available_version()
    {
        var upgrades = await client.GetUpgradesAsync(Timeout());

        Assert.All(upgrades, p => Assert.False(string.IsNullOrWhiteSpace(p.AvailableVersion), p.Id));
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        var progress = new RecordingProgress();

        var outcome = await client.InstallAsync("KolayKur.YokBoylePaket", progress, Timeout());

        Assert.Equal(OperationStatus.NotFound, outcome.Status);
    }

    /// <summary>Makineyi değiştirir. Yalnızca KOLAYKUR_REAL_INSTALL=1 ve 7-Zip yüklü değilken çalışır.</summary>
    [RealInstallFact("7-Zip")]
    public async Task Install_then_uninstall_7zip()
    {
        // İkinci güvence: kapıdan geçse bile var olan bir 7-Zip'i asla silme.
        var installedBefore = await client.GetInstalledAsync(Timeout());
        Assert.DoesNotContain(installedBefore, p =>
            string.Equals(p.Id, SevenZipId, StringComparison.OrdinalIgnoreCase)
            || p.Name.StartsWith("7-Zip", StringComparison.OrdinalIgnoreCase));

        var installProgress = new RecordingProgress();
        var install = await client.InstallAsync(SevenZipId, installProgress, Timeout());
        var uninstallProgress = new RecordingProgress();
        var uninstall = await client.UninstallAsync(SevenZipId, uninstallProgress, Timeout());

        Assert.Equal(OperationStatus.Succeeded, install.Status);
        Assert.Equal(OperationStatus.Succeeded, uninstall.Status);
        Assert.True(installProgress.Count > 0, "Kurulum sırasında ilerleme gelmedi.");
        Assert.True(uninstallProgress.Count > 0, "Kaldırma sırasında ilerleme gelmedi.");
    }

    private sealed class RecordingProgress : IProgress<OperationProgress>
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Report(OperationProgress value) => Interlocked.Increment(ref count);
    }
}
