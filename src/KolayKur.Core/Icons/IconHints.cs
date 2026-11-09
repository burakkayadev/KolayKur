namespace KolayKur.Core.Icons;

/// <summary>
/// Bir paketin simgesini bulmak için winget'ten okunan ipuçları. <see cref="CatalogIcons"/> en uygunu başta olacak
/// sırada katalogdaki simge adresleridir; <see cref="Website"/> uygulamanın (ya da yayıncının) sitesidir;
/// <see cref="InstalledIconSource"/> paket kuruluysa yerel simgeyi bulmaya yarayan, yalnızca
/// <see cref="IInstalledIconReader"/>'ın anlayacağı opak metindir.
/// <see cref="Incomplete"/>: sonuç eksik olabilir (örn. Microsoft Store'a ulaşılamadı ve yalnız winget'te arandı);
/// "simge yok" diye önbelleğe alınmaz.
/// </summary>
public sealed record IconHints(IReadOnlyList<Uri> CatalogIcons, Uri? Website, string? InstalledIconSource, bool Incomplete = false)
{
    public static IconHints Empty { get; } = new([], null, null);

    /// <summary>Paket bulunamadı ama bu kesin değil (geçici bir hata yüzünden); <see cref="Empty"/> ile karışmaması için ayrı örnek.</summary>
    public static IconHints Unknown { get; } = new([], null, null, Incomplete: true);
}
