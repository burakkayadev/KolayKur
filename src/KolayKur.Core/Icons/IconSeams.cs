namespace KolayKur.Core.Icons;

/// <summary>
/// Adresten resim baytlarını indirir. Kabul edilmeyen (http, çok büyük, 404 vb.) yanıtlar için <c>null</c> döner;
/// ağ hatasında özel durum atar (geçici hata: sonuç "simge yok" diye önbelleğe alınmaz).
/// </summary>
public interface IIconFetcher
{
    Task<byte[]?> FetchAsync(Uri url, CancellationToken ct);
}

/// <summary>Kurulu programın simgesini yerel dosyalardan okur (internet kullanmaz); bulamazsa <c>null</c>.</summary>
public interface IInstalledIconReader
{
    Task<byte[]?> ReadAsync(string installedIconSource, CancellationToken ct);
}

/// <summary>
/// Resim baytlarını çözüp PNG'ye çevirir. Çözülemeyen ya da <see cref="IconLimits.MinPixels"/> pikselden küçük
/// resimler için <c>null</c> döner.
/// </summary>
public interface IIconDecoder
{
    Task<byte[]?> ToPngAsync(byte[] data, CancellationToken ct);
}

/// <summary>Simge indirme ve önbellek sınırları.</summary>
public static class IconLimits
{
    public const int MaxDownloadBytes = 512 * 1024;
    public const int MinPixels = 16;
    public const int MaxPixels = 256;
    public const int MaxConcurrentDownloads = 4;
    public const int MaxConcurrentHints = 3;
    public const long MaxCacheBytes = 50L * 1024 * 1024;
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(7);
}
