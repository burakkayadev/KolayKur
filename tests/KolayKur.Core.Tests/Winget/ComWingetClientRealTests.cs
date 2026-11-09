using KolayKur.Core.Icons;
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
        // Sürümü "Unknown" olan kayıtlar (örn. Steam oyunları) bilerek sürümsüz döner; çoğu kaydın sürümü vardır.
        Assert.Contains(installed, p => !string.IsNullOrWhiteSpace(p.InstalledVersion));
        Assert.DoesNotContain(installed, p => string.Equals(p.InstalledVersion, "Unknown", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task Search_finds_store_app()
    {
        var results = await client.SearchAsync("netflix", Timeout());

        Assert.Contains(results, p =>
            string.Equals(p.Source, PackageSources.MicrosoftStore, StringComparison.OrdinalIgnoreCase)
            && p.Name.Contains("Netflix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_marks_winget_source()
    {
        var results = await client.SearchAsync("git", Timeout());

        Assert.Contains(results, p => string.Equals(p.Id, "Git.Git", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Source, PackageSources.Winget, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unknown_store_id_is_not_found()
    {
        var outcome = await client.InstallAsync("9ZZZZZZZZZZZ", new RecordingProgress(), Timeout());

        Assert.Equal(OperationStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task Installed_items_carry_source()
    {
        var installed = await client.GetInstalledAsync(Timeout());

        Assert.Contains(installed, p => string.Equals(p.Source, PackageSources.Winget, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Calls_do_not_post_back_to_caller_context()
    {
        var recorder = new RecordingSynchronizationContext();

        var search = await CallWithContext(recorder, () => client.SearchAsync("git", Timeout()));
        var installed = await CallWithContext(recorder, () => client.GetInstalledAsync(Timeout()));

        Assert.NotEmpty(search);
        Assert.NotEmpty(installed);
        Assert.Equal(0, recorder.PostCount);
    }

    /// <summary>
    /// <paramref name="call"/> çağrısını <paramref name="context"/> geçerliyken başlatır (eşzamanlı kısım ve
    /// bekleme sonrası devam bu bağlamı yakalayabilsin diye) ve önceki bağlamı geri koyar.
    /// </summary>
    private static async Task<T> CallWithContext<T>(SynchronizationContext context, Func<Task<T>> call)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        Task<T> task;
        try
        {
            task = call();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        return await task.ConfigureAwait(false);
    }

    /// <summary>Gönderilen işleri sayar ve hemen çalıştırır (böylece bağlamı yakalayan devamlar takılmaz).</summary>
    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private int postCount;

        public int PostCount => Volatile.Read(ref postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref postCount);
            d(state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref postCount);
            d(state);
        }
    }

    /// <summary>
    /// Store paketleri winget COM üst verisinde simge vermiyor (elle denendi: Netflix, WhatsApp, Spotify için Icons boş);
    /// yayıncı sitesi var, simge o siteden gelir.
    /// </summary>
    [Fact]
    public async Task Icon_hints_for_store_app_have_website()
    {
        var hints = await client.GetIconHintsAsync("9WZDNCRFJ3TJ", "msstore", Timeout());

        Assert.NotNull(hints.Website);
    }

    [Fact]
    public async Task Icon_hints_for_winget_package_can_have_catalog_icon()
    {
        var hints = await client.GetIconHintsAsync("Microsoft.PowerToys", "winget", Timeout());

        Assert.NotEmpty(hints.CatalogIcons);
        Assert.All(hints.CatalogIcons, u => Assert.Equal(Uri.UriSchemeHttps, u.Scheme));
    }

    [Fact]
    public async Task Icon_hints_for_git_have_website()
    {
        var hints = await client.GetIconHintsAsync("Git.Git", "winget", Timeout());

        Assert.NotNull(hints.Website);
    }

    [Fact]
    public async Task Icon_hints_for_unknown_id_are_empty()
    {
        var hints = await client.GetIconHintsAsync("No.Such.Package.Ever", null, Timeout());

        Assert.Same(IconHints.Empty, hints);
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
