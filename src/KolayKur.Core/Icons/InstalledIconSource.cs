namespace KolayKur.Core.Icons;

/// <summary>
/// <see cref="IconHints.InstalledIconSource"/> metninin biçimi: satır satır <c>pfn:&lt;paket ailesi&gt;</c> (MSIX),
/// <c>product:&lt;kaldırma kaydı anahtarı ya da ürün kodu&gt;</c> ve <c>location:&lt;kurulum klasörü&gt;</c>. Hiçbiri yoksa <c>null</c>.
/// </summary>
public static class InstalledIconSource
{
    public static string? Compose(IEnumerable<string> packageFamilyNames, IEnumerable<string> productCodes, string? installedLocation)
    {
        var lines = packageFamilyNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => "pfn:" + n.Trim())
            .Concat(productCodes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => "product:" + c.Trim()))
            .Concat(string.IsNullOrWhiteSpace(installedLocation) ? [] : ["location:" + installedLocation.Trim()])
            .ToList();
        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    public static IEnumerable<string> Values(string source, string kind) =>
        source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.StartsWith(kind + ":", StringComparison.Ordinal))
            .Select(l => l[(kind.Length + 1)..]);
}
