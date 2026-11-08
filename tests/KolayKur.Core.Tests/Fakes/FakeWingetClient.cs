using System.Collections.Concurrent;
using KolayKur.Core.Models;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Fakes;

/// <summary>
/// Sahte winget istemcisi. Paket başına sonuç (<see cref="SetOutcome"/>) ve bekletme (<see cref="Hold"/>)
/// ayarlanabilir; çağrılar <see cref="Calls"/> içinde başlama sırasıyla görünür.
/// Paket kimlikleri büyük/küçük harf ayırmaz.
/// </summary>
public sealed class FakeWingetClient : IWingetClient
{
    private readonly ConcurrentDictionary<string, OperationOutcome> outcomes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FakeGate> gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FakeGate> searchGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<FakeCall> calls = new();
    private readonly ConcurrentQueue<FakeSearchCall> searchCalls = new();
    private int upgradeListCalls;
    private int installedListCalls;
    private int running;
    private int maxConcurrent;

    /// <summary><see cref="SearchAsync"/> bu listeyi kimlik veya ada göre süzer.</summary>
    public IReadOnlyList<PackageInfo> Catalog { get; set; } = [];

    public IReadOnlyList<PackageInfo> Installed { get; set; } = [];

    public IReadOnlyList<PackageInfo> Upgrades { get; set; } = [];

    /// <summary>Doluysa arama ve liste çağrıları bunu fırlatır (örn. <see cref="CatalogUnavailableException"/>).</summary>
    public Exception? ListException { get; set; }

    /// <summary>Sonucu belirlenmemiş paketler için dönen sonuç.</summary>
    public OperationOutcome DefaultOutcome { get; set; } = new(OperationStatus.Succeeded, null);

    /// <summary>İşlemlerin başlama sırası.</summary>
    public IReadOnlyList<FakeCall> Calls => [.. calls];

    /// <summary><see cref="SearchAsync"/> çağrıları (sorgu ve iptal belirteciyle), başlama sırasıyla.</summary>
    public IReadOnlyList<FakeSearchCall> SearchCalls => [.. searchCalls];

    /// <summary><see cref="GetUpgradesAsync"/> kaç kez çağrıldı.</summary>
    public int UpgradeListCalls => Volatile.Read(ref upgradeListCalls);

    /// <summary><see cref="GetInstalledAsync"/> kaç kez çağrıldı.</summary>
    public int InstalledListCalls => Volatile.Read(ref installedListCalls);

    /// <summary>Aynı anda çalışan işlem sayısının görülen en yüksek değeri.</summary>
    public int MaxConcurrent => Volatile.Read(ref maxConcurrent);

    /// <summary>Verilen paketin işlemi bu sonucu döndürür.</summary>
    public void SetOutcome(string packageId, OperationOutcome outcome) => outcomes[packageId] = outcome;

    /// <summary>
    /// Verilen paketin işlemini <see cref="FakeGate.Release"/> çağrılana kadar bekletir.
    /// Test işlem başlayana kadar <see cref="FakeGate.Started"/> görevini bekleyebilir.
    /// </summary>
    public FakeGate Hold(string packageId) => gates.GetOrAdd(packageId, _ => new FakeGate());

    /// <summary>
    /// Verilen sorgunun aramasını <see cref="FakeGate.Release"/> çağrılana kadar bekletir. Bekleme iptal belirtecini
    /// bilerek yok sayar: iptali önemsemeyen en kötü durumdaki istemci, eski aramanın yeni aramadan sonra bitmesini sağlar.
    /// </summary>
    public FakeGate HoldSearch(string query) => searchGates.GetOrAdd(query, _ => new FakeGate());

    public async Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct)
    {
        searchCalls.Enqueue(new FakeSearchCall(query, ct));
        if (searchGates.TryGetValue(query, out var gate))
        {
            gate.MarkStarted();
            await gate.Released;
        }

        return await List(Catalog.Where(p =>
            p.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref installedListCalls);
        return List(Installed);
    }

    public Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref upgradeListCalls);
        return List(Upgrades);
    }

    public Task<OperationOutcome> InstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
        RunAsync(OperationKind.Install, packageId, ProgressStage.Installing, progress, ct);

    public Task<OperationOutcome> UpgradeAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
        RunAsync(OperationKind.Upgrade, packageId, ProgressStage.Installing, progress, ct);

    public Task<OperationOutcome> UninstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
        RunAsync(OperationKind.Uninstall, packageId, ProgressStage.Uninstalling, progress, ct);

    private Task<IReadOnlyList<PackageInfo>> List(IEnumerable<PackageInfo> source) =>
        ListException is null
            ? Task.FromResult<IReadOnlyList<PackageInfo>>([.. source])
            : Task.FromException<IReadOnlyList<PackageInfo>>(ListException);

    private async Task<OperationOutcome> RunAsync(
        OperationKind kind, string packageId, string stage, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        calls.Enqueue(new FakeCall(kind, packageId));
        var now = Interlocked.Increment(ref running);
        InterlockedMax(ref maxConcurrent, now);
        try
        {
            progress.Report(new OperationProgress(50, stage));
            if (gates.TryGetValue(packageId, out var gate))
            {
                gate.MarkStarted();
                await gate.Released.WaitAsync(ct);
            }

            progress.Report(new OperationProgress(100, ProgressStage.Finished));
            return outcomes.GetValueOrDefault(packageId, DefaultOutcome);
        }
        finally
        {
            Interlocked.Decrement(ref running);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (value <= seen)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, value, seen) != seen);
    }
}

public sealed record FakeCall(OperationKind Kind, string PackageId);

public sealed record FakeSearchCall(string Query, CancellationToken Token);

/// <summary><see cref="FakeWingetClient.Hold"/> ile alınan bekletme kapısı.</summary>
public sealed class FakeGate
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>İşlem kapıya ulaşıp beklemeye başlayınca tamamlanır.</summary>
    public Task Started => started.Task;

    internal Task Released => released.Task;

    /// <summary>Bekleyen (veya ileride gelecek) işlemi serbest bırakır.</summary>
    public void Release() => released.TrySetResult();

    internal void MarkStarted() => started.TrySetResult();
}
