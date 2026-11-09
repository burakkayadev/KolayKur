namespace KolayKur.Core.Icons;

/// <summary>
/// Bir paketin simgesini bulmak için winget'ten okunan ipuçları. <see cref="CatalogIcons"/> en uygunu başta olacak
/// sırada katalogdaki simge adresleridir; <see cref="Website"/> uygulamanın (ya da yayıncının) sitesidir;
/// <see cref="InstalledIconSource"/> paket kuruluysa yerel simgeyi bulmaya yarayan, yalnızca
/// <see cref="IInstalledIconReader"/>'ın anlayacağı opak metindir.
/// </summary>
public sealed record IconHints(IReadOnlyList<Uri> CatalogIcons, Uri? Website, string? InstalledIconSource)
{
    public static IconHints Empty { get; } = new([], null, null);
}
