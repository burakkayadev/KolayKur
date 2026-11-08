using System.Globalization;
using System.Runtime.InteropServices;
using KolayKur.Core.Models;
using Microsoft.Management.Deployment;

namespace KolayKur.Core.Winget;

/// <summary>
/// <see cref="IWingetClient"/> uygulaması: winget'in resmi COM arayüzünü kullanır. Bütün COM çağrıları
/// WinRT eşzamansız işlemlerinin <c>await</c> edilmesiyle yapılır, çağıran iş parçacığı bloke edilmez.
/// </summary>
public sealed class ComWingetClient(WingetComFactory factory) : IWingetClient
{
    private const int SearchLimit = 50;
    private const double PercentScale = 100;

    public async Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct)
    {
        // Yalnızca uzak (winget) paketler döner, ama yüklü katalogla eşleşenlerin InstalledVersion alanı dolar.
        // (RemotePackagesFromRemoteCatalogs ile bu alan boş kalıyordu; elle denenip doğrulandı.)
        var (_, catalog) = await ConnectCompositeAsync(CompositeSearchBehavior.RemotePackagesFromAllCatalogs, ct);
        var filter = MatchFilter(PackageMatchField.CatalogDefault, PackageFieldMatchOption.ContainsCaseInsensitive, query);
        var packages = await FindAsync(catalog, filter, SearchLimit, ct);
        return [.. packages.Take(SearchLimit).Select(ToInfo)];
    }

    public async Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct) =>
        [.. (await FindInstalledAsync(ct)).Select(ToInfo)];

    public async Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct) =>
        [.. (await FindInstalledAsync(ct)).Where(p => p.IsUpdateAvailable).Select(ToInfo)];

    public async Task<OperationOutcome> InstallAsync(
        string packageId, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var (manager, catalog) = await ConnectRemoteAsync(ct);
        var package = await FindExactAsync(catalog, packageId, ct);
        if (package is null)
        {
            return NotFound(packageId);
        }

        var options = CreateInstallOptions();
        return await RunInstallAsync(() => manager.InstallPackageAsync(package, options), progress, ct);
    }

    public async Task<OperationOutcome> UpgradeAsync(
        string packageId, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var (manager, catalog) = await ConnectCompositeAsync(CompositeSearchBehavior.LocalCatalogs, ct);
        var package = await FindExactAsync(catalog, packageId, ct);
        if (package?.InstalledVersion is null)
        {
            return NotFound(packageId);
        }

        var options = CreateInstallOptions();
        return await RunInstallAsync(() => manager.UpgradePackageAsync(package, options), progress, ct);
    }

    public async Task<OperationOutcome> UninstallAsync(
        string packageId, IProgress<OperationProgress> progress, CancellationToken ct)
    {
        var (manager, catalog) = await ConnectCompositeAsync(CompositeSearchBehavior.LocalCatalogs, ct);
        var package = await FindExactAsync(catalog, packageId, ct);
        if (package?.InstalledVersion is null)
        {
            return NotFound(packageId);
        }

        var options = factory.CreateUninstallOptions();
        options.PackageUninstallMode = PackageUninstallMode.Silent;
        try
        {
            var result = await manager.UninstallPackageAsync(package, options)
                .AsTask(ct, new InlineProgress<UninstallProgress>(p => progress.Report(ToProgress(p))));
            return WingetOutcomeMapper.MapUninstall(
                result.Status, HResultOf(result.ExtendedErrorCode), result.UninstallerErrorCode);
        }
        catch (COMException ex)
        {
            return FromComFailure(ex);
        }
    }

    private async Task<OperationOutcome> RunInstallAsync(
        Func<Windows.Foundation.IAsyncOperationWithProgress<InstallResult, InstallProgress>> start,
        IProgress<OperationProgress> progress,
        CancellationToken ct)
    {
        try
        {
            var result = await start().AsTask(ct, new InlineProgress<InstallProgress>(p => progress.Report(ToProgress(p))));
            return WingetOutcomeMapper.MapInstall(
                result.Status, HResultOf(result.ExtendedErrorCode), result.InstallerErrorCode);
        }
        catch (COMException ex)
        {
            return FromComFailure(ex);
        }
    }

    /// <summary>Yalnızca uzak <c>winget</c> kataloğuna bağlanır.</summary>
    private Task<(PackageManager Manager, PackageCatalog Catalog)> ConnectRemoteAsync(CancellationToken ct) =>
        ConnectAsync((manager, remote) => remote, ct);

    /// <summary><c>winget</c> kataloğunu yüklü katalogla birleştiren bir katalog açar.</summary>
    private Task<(PackageManager Manager, PackageCatalog Catalog)> ConnectCompositeAsync(
        CompositeSearchBehavior behavior, CancellationToken ct) =>
        ConnectAsync(
            (manager, remote) =>
            {
                var composite = factory.CreateCompositeOptions();
                composite.Catalogs.Add(remote);
                composite.CompositeSearchBehavior = behavior;
                return manager.CreateCompositePackageCatalog(composite);
            },
            ct);

    private async Task<(PackageManager Manager, PackageCatalog Catalog)> ConnectAsync(
        Func<PackageManager, PackageCatalogReference, PackageCatalogReference> select, CancellationToken ct)
    {
        try
        {
            var manager = factory.CreatePackageManager();
            var remote = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
            var connect = await select(manager, remote).ConnectAsync().AsTask(ct);
            return connect.Status == ConnectResultStatus.Ok
                ? (manager, connect.PackageCatalog)
                : throw new CatalogUnavailableException($"winget kataloğuna bağlanılamadı: {connect.Status}.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or CatalogUnavailableException))
        {
            throw new CatalogUnavailableException($"winget kataloğuna bağlanılamadı: {ex.Message}", ex);
        }
    }

    private async Task<CatalogPackage?> FindExactAsync(PackageCatalog catalog, string packageId, CancellationToken ct)
    {
        var filter = MatchFilter(PackageMatchField.Id, PackageFieldMatchOption.EqualsCaseInsensitive, packageId);
        var matches = await FindAsync(catalog, filter, 0, ct);
        return matches.FirstOrDefault(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Yüklü katalogdaki her paket (winget kataloğundaki eşleşmeleriyle birlikte).</summary>
    private async Task<IReadOnlyList<CatalogPackage>> FindInstalledAsync(CancellationToken ct)
    {
        var (_, catalog) = await ConnectCompositeAsync(CompositeSearchBehavior.LocalCatalogs, ct);
        return await FindAsync(catalog, null, 0, ct);
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
            var result = await catalog.FindPackagesAsync(options).AsTask(ct);
            return result.Status == FindPackagesResultStatus.Ok
                ? PackagesOf(result.Matches)
                : throw new CatalogUnavailableException($"winget araması başarısız: {result.Status}.");
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or CatalogUnavailableException))
        {
            throw new CatalogUnavailableException($"winget araması başarısız: {ex.Message}", ex);
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
            NullIfEmpty(installed?.Version),
            NullIfEmpty(available?.Version));
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static OperationOutcome NotFound(string packageId) =>
        new(OperationStatus.NotFound, $"{packageId} bulunamadı.");

    private static int HResultOf(Exception? extendedErrorCode) => extendedErrorCode?.HResult ?? 0;

    private static OperationOutcome FromComFailure(COMException ex)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"winget çağrısı başarısız (0x{ex.HResult:X8}): {ex.Message}");
        return WingetOutcomeMapper.IsCancellation(ex.HResult, 0)
            ? new OperationOutcome(OperationStatus.Cancelled, message)
            : new OperationOutcome(OperationStatus.Failed, message);
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
