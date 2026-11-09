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
    private const int PreferredIconPixels = 64;

    // Önce kurulu programlar, sonra uzak kataloglar; Store kimlikleri bazen yalnız son kipte eşleşir (kurma yolundaki gibi).
    private static readonly CompositeSearchBehavior[] HintBehaviors =
    [
        CompositeSearchBehavior.LocalCatalogs,
        CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
        CompositeSearchBehavior.RemotePackagesFromRemoteCatalogs,
    ];

    private readonly ConcurrentDictionary<(bool Store, CompositeSearchBehavior Behavior), (DateTime Created, Task<PackageCatalog> Catalog)> hintCatalogs = new();

    public Task<IconHints> GetIconHintsAsync(string packageId, string? source, CancellationToken ct) =>
        Task.Run(() => GetIconHintsCoreAsync(packageId, source, ct), ct);

    private async Task<IconHints> GetIconHintsCoreAsync(string packageId, string? source, CancellationToken ct)
    {
        Task<CatalogPackage?> Find(bool includeStore) => FindForHintsAsync(includeStore, packageId, ct);
        var package = string.Equals(source, PackageSources.Winget, StringComparison.OrdinalIgnoreCase)
            ? await Find(false).ConfigureAwait(false)
            : await StoreFallback.RunAsync(Find, log, ct).ConfigureAwait(false);
        return package is null ? IconHints.Empty : ToIconHints(package);
    }

    /// <summary>Önce yüklü katalogda (kurulu programlar, kurulu sürüm bilgisiyle), yoksa uzak kataloglarda arar.</summary>
    private async Task<CatalogPackage?> FindForHintsAsync(bool includeStore, string packageId, CancellationToken ct)
    {
        foreach (var behavior in HintBehaviors)
        {
            var catalog = await HintCatalogAsync(includeStore, behavior, ct).ConfigureAwait(false);
            var found = await FindExactForHintsAsync(catalog, packageId, ct).ConfigureAwait(false);
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
    private async Task<CatalogPackage?> FindExactForHintsAsync(PackageCatalog catalog, string packageId, CancellationToken ct)
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

    /// <summary>Çok sayıda kart için her seferinde yeniden bağlanmamak üzere bağlantı kısa süre paylaşılır; başarısız bağlantı saklanmaz.</summary>
    private async Task<PackageCatalog> HintCatalogAsync(bool includeStore, CompositeSearchBehavior behavior, CancellationToken ct)
    {
        var key = (includeStore, behavior);
        var now = DateTime.UtcNow;
        var entry = hintCatalogs.AddOrUpdate(
            key,
            _ => (now, ConnectCatalogAsync(includeStore, behavior)),
            (_, old) => now - old.Created > HintCatalogLifetime || old.Catalog.IsFaulted
                ? (now, ConnectCatalogAsync(includeStore, behavior))
                : old);
        try
        {
            return await entry.Catalog.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            hintCatalogs.TryRemove(new KeyValuePair<(bool, CompositeSearchBehavior), (DateTime, Task<PackageCatalog>)>(key, entry));
            throw;
        }
    }

    // Paylaşılan bağlantı tek bir çağıranın iptaline bağlanmaz.
    private async Task<PackageCatalog> ConnectCatalogAsync(bool includeStore, CompositeSearchBehavior behavior)
    {
        var (_, catalog) = await Task.Run(() => ConnectCompositeAsync(includeStore, behavior, CancellationToken.None)).ConfigureAwait(false);
        return catalog;
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

    /// <summary>Uygun boyutlu (en az 64 px, yoksa en büyük), tarafsız temalı simgeler önce; yalnız https adresleri.</summary>
    private static List<Uri> RankIcons(IReadOnlyList<Icon> icons)
    {
        var candidates = new List<(Uri Url, int Pixels, bool Neutral)>();
        for (var i = 0; i < icons.Count; i++)
        {
            var icon = icons[i];
            if (Uri.TryCreate(icon.Url, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
            {
                candidates.Add((url, PixelsOf(icon.Resolution), icon.Theme is IconTheme.Default));
            }
        }

        return [.. candidates
            .OrderByDescending(c => c.Neutral)
            .ThenBy(c => c.Pixels >= PreferredIconPixels ? 0 : 1)
            .ThenBy(c => c.Pixels >= PreferredIconPixels ? c.Pixels : -c.Pixels)
            .Select(c => c.Url)];
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
