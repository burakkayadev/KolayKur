namespace KolayKur.Core.Support;

/// <summary>Paket kartındaki harf avatarı: görünen harf ve palet sırası.</summary>
public sealed record AvatarInfo(string Letter, int ColorIndex);

/// <summary>
/// winget uygulama ikonu vermediği için her pakete adından harf, kimliğinden sabit bir renk sırası verir.
/// Renk sırası süreçten sürece değişmesin diye <c>string.GetHashCode()</c> yerine FNV-1a kullanılır.
/// </summary>
public static class PackageAvatar
{
    public const int PaletteSize = 8;

    private const string UnknownLetter = "?";
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    public static AvatarInfo For(string id, string name) => new(LetterOf(name), ColorIndexOf(id));

    private static string LetterOf(string name)
    {
        foreach (var ch in (name ?? string.Empty).Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                // .NET'in değişmez kültürü noktasız ı'yı büyütmez; Unicode'a göre büyük karşılığı I'dır.
                return ch == 'ı' ? "I" : char.ToUpperInvariant(ch).ToString();
            }
        }

        return UnknownLetter;
    }

    private static int ColorIndexOf(string id)
    {
        var hash = FnvOffsetBasis;
        foreach (var ch in (id ?? string.Empty).ToUpperInvariant())
        {
            hash = unchecked((hash ^ ch) * FnvPrime);
        }

        return (int)(hash % PaletteSize);
    }
}
