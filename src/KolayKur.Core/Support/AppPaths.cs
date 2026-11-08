namespace KolayKur.Core.Support;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KolayKur");

    public static string Logs { get; } = Path.Combine(Root, "logs");

    public static string Downloads { get; } = Path.Combine(Root, "downloads");

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
}
