namespace KolayKur.Core.Support;

public static class AppPaths
{
    /// <summary>Veri klasörünü değiştiren ortam değişkeni (ekran görüntüsü betiği ve testler için). Yalnızca mutlak yol kabul edilir.</summary>
    public const string DataDirVariable = "KOLAYKUR_DATA_DIR";

    // Her erişimde okunur: testler değişkeni çalışma sırasında değiştirir.
    public static string Root
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(DataDirVariable);
            if (IsUnderTemp(overridden))
            {
                return overridden!;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KolayKur");
        }
    }

    // Yalnızca mutlak ve kullanıcının geçici klasörü altındaki yollar kabul edilir (veri klasörü değişkeni kötüye kullanılamasın).
    private static bool IsUnderTemp(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
        {
            return false;
        }

        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && full.Length > temp.Length;
    }

    public static string Logs => Path.Combine(Root, "logs");

    public static string Downloads => Path.Combine(Root, "downloads");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
