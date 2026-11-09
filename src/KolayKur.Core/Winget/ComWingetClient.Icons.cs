using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using KolayKur.Core.Icons;
using KolayKur.Core.Models;
using KolayKur.Core.Support;
using Microsoft.Management.Deployment;

namespace KolayKur.Core.Winget;

public sealed partial class ComWingetClient
{
    private static readonly TimeSpan HintCatalogLifetime = TimeSpan.FromMinutes(10);
    private const int PreferredIconPixels = IconRanking.PreferredPixels;

    // Önce kurulu programlar, sonra uzak kataloglar; Store kimlikleri bazen yalnız son kipte eşleşir (kurma yolundaki gibi).
    private static readonly CompositeSearchBehavior[] HintBehaviors =
    [
        CompositeSearchBehavior.LocalCatalogs,
        CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
        CompositeSearchBehavior.RemotePackagesFromRemoteCatalogs,
    ];

    // Başarısız bağlantı da kısa süre hatırlanır: 120 kart yavaş Store bağlantısını tek tek yeniden denemesin.
    private static readonly TimeSpan HintFailureLifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<(bool Store, CompositeSearchBehavior Behavior), HintCatalogEntry> hintCatalogs = new();

    public Task<IconHints> GetIconHintsAsync(string packageId, string? source, CancellationToken ct) =>
        Task.Run(() => GetIconHintsCoreAsync(packageId, source, ct), ct);

    private async Task<IconHints> GetIconHintsCoreAsync(string packageId, string? source, CancellationToken ct)
    {
        var storeSkipped = false;
        Task<CatalogPackage?> Find(bool includeStore)
        {
            storeSkipped = !includeStore;
            return FindForHintsAsync(includeStore, packageId, ct);
        }

        var wingetOnly = string.Equals(source, PackageSources.Winget, StringComparison.OrdinalIgnoreCase);
        var package = wingetOnly
            ? await Find(false).ConfigureAwait(false)
            : await StoreFallback.RunAsync(Find, log, ct).ConfigureAwait(false);

        // Store'a ulaşılamayıp yalnız winget'te aranan paket (Store kimliği olabilir) bulunamadıysa "yok" denemez.
        return package is not null ? ToIconHints(package) : !wingetOnly && storeSkipped ? IconHints.Unknown : IconHints.Empty;
    }

    /// <summary>Önce yüklü katalogda (kurulu programlar, kurulu sürüm bilgisiyle), yoksa uzak kataloglarda arar.</summary>
    private async Task<CatalogPackage?> FindForHintsAsync(bool includeStore, string packageId, CancellationToken ct)
    {
        foreach (var behavior in HintBehaviors)
        {
            var catalog = await HintCatalogAsync(includeStore, behavior, ct).ConfigureAwait(false);
            var found = await FindExactAsync(catalog, packageId, ct).ConfigureAwait(false);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Kesin kimlik araması. Microsoft Store kimlikleri (örn. 9WZDNCRFJ3TJ) <c>EqualsCaseInsensitive</c> ile eşleşmez
    /// (elle denendi: 0 sonuç), <c>Equals</c> ile eşleşir; bu yüzden önce o, sonra büyük/küçük harf duyarsız denenir.
    /// </summary>
    private async Task<CatalogPackage?> FindExactAsync(PackageCatalog catalog, string packageId, CancellationToken ct)
    {
        foreach (var option in new[] { PackageFieldMatchOption.Equals, PackageFieldMatchOption.EqualsCaseInsensitive })
        {
            var matches = await FindAsync(catalog, MatchFilter(PackageMatchField.Id, option, packageId), 0, ct).ConfigureAwait(false);
            var found = matches.FirstOrDefault(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Çok sayıda kart için her seferinde yeniden bağlanmamak üzere bağlantı paylaşılır (başarılıysa 10 dk, başarısızsa 2 dk).
    /// Bağlantı ilk isteyen çıkana kadar başlatılmaz (<see cref="Lazy{T}"/>), böylece yarışta yinelenen bağlantı açılmaz.
    /// </summary>
    private async Task<PackageCatalog> HintCatalogAsync(bool includeStore, CompositeSearchBehavior behavior, CancellationToken ct)
    {
        var key = (includeStore, behavior);
        var now = DateTime.UtcNow;
        var entry = hintCatalogs.AddOrUpdate(
            key,
            _ => new HintCatalogEntry(now, NewConnection(includeStore, behavior)),
            (_, old) => old.IsExpired(now) ? new HintCatalogEntry(now, NewConnection(includeStore, behavior)) : old);
        return await entry.Connection.Value.WaitAsync(ct).ConfigureAwait(false);
    }

    // Paylaşılan bağlantı tek bir çağıranın iptaline bağlanmaz.
    private Lazy<Task<PackageCatalog>> NewConnection(bool includeStore, CompositeSearchBehavior behavior) =>
        new(() => Task.Run(async () =>
        {
            var (_, catalog) = await ConnectCompositeAsync(includeStore, behavior, CancellationToken.None).ConfigureAwait(false);
            return catalog;
        }));

    private sealed record HintCatalogEntry(DateTime Created, Lazy<Task<PackageCatalog>> Connection)
    {
        public bool IsExpired(DateTime now) =>
            now - Created > (Connection.IsValueCreated && Connection.Value.IsFaulted ? HintFailureLifetime : HintCatalogLifetime);
    }

    private static IconHints ToIconHints(CatalogPackage package)
    {
        var available = package.DefaultInstallVersion;
        var installed = package.InstalledVersion;
        var icons = new List<Uri>();
        Uri? website = null;
        try
        {
            var metadata = (available ?? installed)?.GetCatalogPackageMetadata();
            if (metadata is not null)
            {
                icons = RankIcons(metadata.Icons);
                website = FirstUsableSite(metadata.PackageUrl, metadata.PublisherUrl);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            // Bu paketin katalog üst verisi okunamadı: kalan kaynaklarla devam edilir.
        }

        return new IconHints(icons, website, InstalledSourceOf(installed));
    }

    /// <summary>Katalogdaki simgeleri sıralar; yalnız https adresleri, yüksek kontrast temalılar atılır.</summary>
    private static List<Uri> RankIcons(IReadOnlyList<Icon> icons)
    {
        var candidates = new List<IconCandidate>();
        for (var i = 0; i < icons.Count; i++)
        {
            var icon = icons[i];
            if (Uri.TryCreate(icon.Url, UriKind.Absolute, out var url))
            {
                candidates.Add(new IconCandidate(url, PixelsOf(icon.Resolution), icon.Theme switch
                {
                    IconTheme.Light => IconThemeRank.Light,
                    IconTheme.Dark => IconThemeRank.Dark,
                    IconTheme.HighContrast => IconThemeRank.HighContrast,
                    _ => IconThemeRank.Default,
                }));
            }
        }

        return IconRanking.Rank(candidates);
    }

    private static int PixelsOf(IconResolution resolution) => resolution switch
    {
        IconResolution.Square16 => 16,
        IconResolution.Square20 => 20,
        IconResolution.Square24 => 24,
        IconResolution.Square30 => 30,
        IconResolution.Square32 => 32,
        IconResolution.Square36 => 36,
        IconResolution.Square40 => 40,
        IconResolution.Square48 => 48,
        IconResolution.Square60 => 60,
        IconResolution.Square64 => 64,
        IconResolution.Square72 => 72,
        IconResolution.Square80 => 80,
        IconResolution.Square96 => 96,
        IconResolution.Square256 => 256,
        _ => PreferredIconPixels,
    };

    private static Uri? FirstUsableSite(params string?[] candidates)
    {
        foreach (var text in candidates)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var url) && IconSiteRules.IsUsableWebsite(url))
            {
                return url;
            }
        }

        return null;
    }

    private static string? InstalledSourceOf(PackageVersionInfo? installed)
    {
        if (installed is null)
        {
            return null;
        }

        try
        {
            return InstalledIconSource.Compose(
                [.. installed.PackageFamilyNames],
                [.. installed.ProductCodes],
                installed.GetMetadata(PackageVersionMetadataField.InstalledLocation));
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
        {
            return null;
        }
    }
}
