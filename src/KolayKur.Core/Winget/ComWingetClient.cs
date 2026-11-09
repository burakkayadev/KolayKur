using System.Globalization;
using System.Runtime.InteropServices;
using KolayKur.Core.Models;
using KolayKur.Core.Support;
using Microsoft.Management.Deployment;

namespace KolayKur.Core.Winget;

/// <summary>
/// <see cref="IWingetClient"/> uygulaması: winget'in resmi COM arayüzünü kullanır. Bütün COM çağrıları
/// WinRT eşzamansız işlemlerinin <c>await</c> edilmesiyle yapılır. Her genel yöntemin gövdesi
/// <see cref="Task.Run(Func{Task}, CancellationToken)"/> ile iş parçacığı havuzunda başlar ve her <c>await</c>
/// <c>ConfigureAwait(false)</c> kullanır: eşzamanlı COM çağrıları (nesne oluşturma, liste okuma) ve devamlar
/// çağıranın <see cref="SynchronizationContext"/> bağlamında (örn. arayüz iş parçacığı) çalışmaz.
/// </summary>
public sealed partial class ComWingetClient(WingetComFactory factory, FileLog? log = null) : IWingetClient
{
    private const int SearchLimit = 50;
    private const double PercentScale = 100;

    public Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct) =>
        Task.Run(() => SearchCoreAsync(query, ct), ct);

    public Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct) =>
        Task.Run(() => GetInstalledCoreAsync(ct), ct);

    public Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct) =>
        Task.Run(() => GetUpgradesCoreAsync(ct), ct);

    public Task<OperationOutcome> InstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
        Task.Run(() => InstallCoreAsync(packageId, progress, ct), ct);

    public Task<OperationOutcome> UpgradeAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
        Task.Run(() => UpgradeCoreAsync(packageId, progress, ct), ct);

    public Task<OperationOutcome> UninstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
        Task.Run(() => UninstallCoreAsync(packageId, progress, ct), ct);

    private async Task<IReadOnlyList<PackageInfo>> SearchCoreAsync(string query, CancellationToken ct)
    {
        var packages = await StoreFallback.RunAsync(
            includeStore => SearchOnceAsync(includeStore, query, ct), log, ct).ConfigureAwait(false);
        return [.. packages.Take(SearchLimit).Select(ToInfo)];
    }

    private async Task<IReadOnlyList<CatalogPackage>> SearchOnceAsync(bool includeStore, string query, CancellationToken ct)
    {
        // Yalnızca uzak paketler döner, ama yüklü katalogla eşleşenlerin InstalledVersion alanı dolar.
        // (RemotePackagesFromRemoteCatalogs ile bu alan boş kalıyordu; elle denenip doğrulandı.)
        var (_, catalog) = await ConnectCompositeAsync(
            includeStore, CompositeSearchBehavior.RemotePackagesFromAllCatalogs, ct).ConfigureAwait(false);
        var filter = MatchFilter(PackageMatchField.CatalogDefault, PackageFieldMatchOption.ContainsCaseInsensitive, query);
        return await FindAsync(catalog, filter, SearchLimit, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<PackageInfo>> GetInstalledCoreAsync(CancellationToken ct) =>
        [.. (await FindInstalledAsync(ct).ConfigureAwait(false)).Select(ToInfo)];

    private async Task<IReadOnlyList<PackageInfo>> GetUpgradesCoreAsync(CancellationToken ct) =>
        [.. (await FindInstalledAsync(ct).ConfigureAwait(false)).Where(p => p.IsUpdateAvailable).Select(ToInfo)];

    private async Task<OperationOutcome> InstallCoreAsync(
        string packageId, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var (manager, package) = await ResolveExactAsync(
            includeStore => ConnectRemoteAsync(includeStore, ct), packageId, ct).ConfigureAwait(false);
        if (package is null)
        {
            return NotFound(packageId);
        }

        var options = CreateInstallOptions();
        return await RunInstallAsync(OperationKind.Install, () => manager.InstallPackageAsync(package, options), progress, ct).ConfigureAwait(false);
    }

    private async Task<OperationOutcome> UpgradeCoreAsync(
        string packageId, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var (manager, package) = await ResolveExactAsync(
            includeStore => ConnectCompositeAsync(includeStore, CompositeSearchBehavior.LocalCatalogs, ct),
            packageId,
            ct).ConfigureAwait(false);
        if (package?.InstalledVersion is null)
        {
            return NotFound(packageId);
        }

        var options = CreateInstallOptions();
        return await RunInstallAsync(OperationKind.Upgrade, () => manager.UpgradePackageAsync(package, options), progress, ct).ConfigureAwait(false);
    }

    private async Task<OperationOutcome> UninstallCoreAsync(
        string packageId, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var (manager, package) = await ResolveExactAsync(
            includeStore => ConnectCompositeAsync(includeStore, CompositeSearchBehavior.LocalCatalogs, ct),
            packageId,
            ct).ConfigureAwait(false);
        if (package?.InstalledVersion is null)
        {
            return NotFound(packageId);
        }

        var options = factory.CreateUninstallOptions();
        options.PackageUninstallMode = PackageUninstallMode.Silent;
        try
        {
            var result = await manager.UninstallPackageAsync(package, options)
                .AsTask(ct, new InlineProgress<UninstallProgress>(p => progress.Report(ToProgress(p))))
                .ConfigureAwait(false);
            return WingetOutcomeMapper.MapUninstall(
                result.Status, HResultOf(result.ExtendedErrorCode), result.UninstallerErrorCode);
        }
        catch (COMException ex)
        {
            return FromComFailure(ex, OperationReason.UninstallFailed);
        }
    }

    private async Task<OperationOutcome> RunInstallAsync(
        OperationKind kind,
        Func<Windows.Foundation.IAsyncOperationWithProgress<InstallResult, InstallProgress>> start,
        IProgress<OperationProgress> progress,
        CancellationToken ct)
    {
        try
        {
            var result = await start()
                .AsTask(ct, new InlineProgress<InstallProgress>(p => progress.Report(ToProgress(p))))
                .ConfigureAwait(false);
            return WingetOutcomeMapper.MapInstall(
                result.Status, HResultOf(result.ExtendedErrorCode), result.InstallerErrorCode, kind);
        }
        catch (COMException ex)
        {
            return FromComFailure(ex, kind == OperationKind.Upgrade ? OperationReason.UpgradeFailed : OperationReason.InstallFailed);
        }
    }

    /// <summary>
    /// Kurma için uzak katalog: yalnız <c>winget</c> ya da (<paramref name="includeStore"/>) <c>winget</c> ile
    /// <c>msstore</c> birlikte (yüklü katalog olmadan).
    /// </summary>
    private Task<(PackageManager Manager, PackageCatalog Catalog)> ConnectRemoteAsync(bool includeStore, CancellationToken ct) =>
        includeStore
            ? ConnectCompositeAsync(true, CompositeSearchBehavior.RemotePackagesFromRemoteCatalogs, ct)
            : ConnectAsync(false, (manager, remotes) => remotes[0], ct);

    /// <summary>Uzak katalogları (winget, istenirse msstore) birleştiren bir katalog açar.</summary>
    private Task<(PackageManager Manager, PackageCatalog Catalog)> ConnectCompositeAsync(
        bool includeStore, CompositeSearchBehavior behavior, CancellationToken ct) =>
        ConnectAsync(
            includeStore,
            (manager, remotes) =>
            {
                var composite = factory.CreateCompositeOptions();
                foreach (var remote in remotes)
                {
                    composite.Catalogs.Add(remote);
                }

                composite.CompositeSearchBehavior = behavior;
                return manager.CreateCompositePackageCatalog(composite);
            },
            ct);

    private async Task<(PackageManager Manager, PackageCatalog Catalog)> ConnectAsync(
        bool includeStore,
        Func<PackageManager, IReadOnlyList<PackageCatalogReference>, PackageCatalogReference> select,
        CancellationToken ct)
    {
        try
        {
            var manager = factory.CreatePackageManager();
            var remotes = new List<PackageCatalogReference>
            {
                manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog),
            };
            if (includeStore)
            {
                var store = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.MicrosoftStore);
                store.AcceptSourceAgreements = true;
                remotes.Add(store);
            }

            var connect = await select(manager, remotes).ConnectAsync().AsTask(ct).ConfigureAwait(false);
            return connect.Status == ConnectResultStatus.Ok
                ? (manager, connect.PackageCatalog)
                : throw new CatalogUnavailableException($"could not connect to the package catalog: {connect.Status}.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or CatalogUnavailableException))
        {
            throw new CatalogUnavailableException($"could not connect to the package catalog: {ex.Message}", ex);
        }
    }

    /// <summary>Önce iki kaynakla, olmazsa yalnız winget ile bağlanıp kesin kimliği arar.</summary>
    private Task<(PackageManager Manager, CatalogPackage? Package)> ResolveExactAsync(
        Func<bool, Task<(PackageManager Manager, PackageCatalog Catalog)>> connect, string packageId, CancellationToken ct) =>
        StoreFallback.RunAsync(
            async includeStore =>
            {
                var (manager, catalog) = await connect(includeStore).ConfigureAwait(false);
                return (manager, await FindExactAsync(catalog, packageId, ct).ConfigureAwait(false));
            },
            log,
            ct);

    private async Task<CatalogPackage?> FindExactAsync(PackageCatalog catalog, string packageId, CancellationToken ct)
    {
        var filter = MatchFilter(PackageMatchField.Id, PackageFieldMatchOption.EqualsCaseInsensitive, packageId);
        var matches = await FindAsync(catalog, filter, 0, ct).ConfigureAwait(false);
        return matches.FirstOrDefault(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Yüklü katalogdaki her paket (winget kataloğundaki eşleşmeleriyle birlikte).</summary>
    private async Task<IReadOnlyList<CatalogPackage>> FindInstalledAsync(CancellationToken ct)
    {
        return await StoreFallback.RunAsync(
            async includeStore =>
            {
                var (_, catalog) = await ConnectCompositeAsync(
                    includeStore, CompositeSearchBehavior.LocalCatalogs, ct).ConfigureAwait(false);
                return await FindAsync(catalog, null, 0, ct).ConfigureAwait(false);
            },
            log,
            ct).ConfigureAwait(false);
    }

    private PackageMatchFilter MatchFilter(PackageMatchField field, PackageFieldMatchOption option, string value)
    {
        var filter = factory.CreateMatchFilter();
        filter.Field = field;
        filter.Option = option;
        filter.Value = value;
        return filter;
    }

    private InstallOptions CreateInstallOptions()
    {
        var options = factory.CreateInstallOptions();
        options.PackageInstallMode = PackageInstallMode.Silent;
        options.AcceptPackageAgreements = true;
        return options;
    }

    /// <summary><paramref name="filter"/> <c>null</c> ise katalogdaki her paket döner. <paramref name="limit"/> 0 ise sınırsızdır.</summary>
    private async Task<IReadOnlyList<CatalogPackage>> FindAsync(
        PackageCatalog catalog, PackageMatchFilter? filter, uint limit, CancellationToken ct)
    {
        try
        {
            var options = factory.CreateFindPackagesOptions();
            if (filter is not null)
            {
                options.Selectors.Add(filter);
            }

            options.ResultLimit = limit;
            var result = await catalog.FindPackagesAsync(options).AsTask(ct).ConfigureAwait(false);
            return result.Status == FindPackagesResultStatus.Ok
                ? PackagesOf(result.Matches)
                : throw new CatalogUnavailableException($"package catalog search failed: {result.Status}.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or CatalogUnavailableException))
        {
            throw new CatalogUnavailableException($"package catalog search failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Dizinle okur: dış süreçten gelen <c>IVectorView</c> listesinin <c>IEnumerable</c> arayüzü (LINQ, foreach)
    /// "Böyle bir arabirim desteklenmiyor" hatasıyla başarısız olur, <c>Count</c> ve dizin erişimi çalışır.
    /// </summary>
    private static IReadOnlyList<CatalogPackage> PackagesOf(IReadOnlyList<MatchResult> matches)
    {
        var packages = new List<CatalogPackage>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            packages.Add(matches[i].CatalogPackage);
        }

        return packages;
    }

    private static PackageInfo ToInfo(CatalogPackage package)
    {
        var installed = package.InstalledVersion;
        var available = package.DefaultInstallVersion;
        return new PackageInfo(
            package.Id,
            package.Name,
            available?.Publisher ?? installed?.Publisher ?? "",
            VersionOrNull(installed?.Version),
            VersionOrNull(available?.Version),
            NullIfEmpty(available?.PackageCatalog?.Info?.Name));
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>Boş ya da winget'in "Unknown" sürümü (sürüm bilinmiyor) için <c>null</c>; arayüz sürüm etiketini gizler.</summary>
    internal static string? VersionOrNull(string? version) =>
        string.Equals(version?.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase) ? null : NullIfEmpty(version);

    private static OperationOutcome NotFound(string packageId) =>
        new(OperationStatus.NotFound, $"{packageId} not found.", OperationReason.NotFound);

    private static int HResultOf(Exception? extendedErrorCode) => extendedErrorCode?.HResult ?? 0;

    // Message dilden bağımsız teknik ayrıntıdır; kullanıcıya ReasonKey'in metni gösterilir.
    private static OperationOutcome FromComFailure(COMException ex, string failedReason)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"winget call failed (0x{ex.HResult:X8}): {ex.Message}");
        return WingetOutcomeMapper.IsCancellation(ex.HResult, 0)
            ? new OperationOutcome(OperationStatus.Cancelled, message, OperationReason.UserCancelled)
            : new OperationOutcome(OperationStatus.Failed, message, failedReason);
    }

    // winget bu ilerleme değerlerini 0-1 aralığında verir; burada 0-100'e çevrilir.
    private static OperationProgress ToProgress(InstallProgress p) => p.State switch
    {
        PackageInstallProgressState.Queued => new(null, ProgressStage.Queued),
        PackageInstallProgressState.Downloading => new(
            p.BytesRequired > 0 ? Percent((double)p.BytesDownloaded / p.BytesRequired) : null, ProgressStage.Downloading),
        PackageInstallProgressState.Installing => new(Percent(p.InstallationProgress), ProgressStage.Installing),
        PackageInstallProgressState.PostInstall => new(null, ProgressStage.PostInstall),
        _ => new(PercentScale, ProgressStage.Finished),
    };

    private static OperationProgress ToProgress(UninstallProgress p) => p.State switch
    {
        PackageUninstallProgressState.Queued => new(null, ProgressStage.Queued),
        PackageUninstallProgressState.Uninstalling => new(Percent(p.UninstallationProgress), ProgressStage.Uninstalling),
        PackageUninstallProgressState.PostUninstall => new(null, ProgressStage.PostUninstall),
        _ => new(PercentScale, ProgressStage.Finished),
    };

    private static double Percent(double fraction) => Math.Clamp(fraction * PercentScale, 0, PercentScale);

    /// <summary><see cref="Progress{T}"/> gibi değil, bağlama göndermeden doğrudan çağıran iş parçacığında çalışır.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
