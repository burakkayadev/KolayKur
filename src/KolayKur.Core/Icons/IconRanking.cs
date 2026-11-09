namespace KolayKur.Core.Icons;

public enum IconThemeRank { Default, Light, Dark, HighContrast }

public sealed record IconCandidate(Uri Url, int Pixels, IconThemeRank Theme);

/// <summary>Katalog simgelerinin sıralaması (saf işlev; winget'ten bağımsız sınanır).</summary>
public static class IconRanking
{
    public const int PreferredPixels = 64;
    public const int IdealPixels = 256;

    /// <summary>
    /// Yalnız https; yüksek kontrast atılır. Sıra: tema (varsayılan, açık, koyu), sonra boyut: en az 64 px olanlar 256'ya en yakından başlar,
    /// 64 pikselden küçükler (en büyük önce) en sonda.
    /// </summary>
    public static List<Uri> Rank(IEnumerable<IconCandidate> candidates) =>
    [
        .. candidates
            .Where(c => c.Url.Scheme == Uri.UriSchemeHttps && c.Theme != IconThemeRank.HighContrast)
            .OrderBy(c => c.Theme)
            .ThenBy(c => c.Pixels >= PreferredPixels ? 0 : 1)
            .ThenBy(c => c.Pixels >= PreferredPixels ? Math.Abs(IdealPixels - c.Pixels) : -c.Pixels)
            .Select(c => c.Url),
    ];
}
