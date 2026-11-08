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
            if (!string.IsNullOrWhiteSpace(overridden) && Path.IsPathRooted(overridden) && Path.IsPathFullyQualified(overridden))
            {
                return overridden;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KolayKur");
        }
    }

    public static string Logs => Path.Combine(Root, "logs");

    public static string Downloads => Path.Combine(Root, "downloads");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
}
