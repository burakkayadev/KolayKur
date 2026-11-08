namespace KolayKur.Core.Health;

/// <summary>
/// winget'in tam yolunu tek yerden verir. Çıplak <c>winget</c> adıyla süreç başlatmak Windows'un önce programın kendi
/// klasörüne ve çalışma klasörüne bakmasına yol açar (İndirilenler'e bırakılmış sahte bir winget.exe çalışırdı);
/// ayrıca onarımdan sonra, çalışan sürecin eski PATH'i yüzünden winget bulunamayabilir.
/// </summary>
public static class WingetLocator
{
    public const string FileName = "winget.exe";

    /// <summary>
    /// <c>%LocalAppData%\Microsoft\WindowsApps\winget.exe</c> (App Installer'ın uygulama takma adı).
    /// <paramref name="localAppData"/> yalnızca testler içindir; verilmezse geçerli kullanıcının klasörü kullanılır.
    /// </summary>
    public static string GetPath(string? localAppData = null) =>
        Path.Combine(
            localAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps",
            FileName);
}
