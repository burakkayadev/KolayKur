using KolayKur.Core.Localization;

namespace KolayKur.Core.Models;

/// <summary>
/// Bir paketin kataloglardan birleştirilmiş görünümü. Sürümler yoksa <c>null</c>.
/// <see cref="Source"/> paketin geldiği katalogun adıdır (<see cref="PackageSources"/>); bilinmiyorsa <c>null</c>.
/// </summary>
public sealed record PackageInfo(
    string Id, string Name, string Publisher, string? InstalledVersion, string? AvailableVersion, string? Source = null)
{
    /// <summary>Ekran okuyucu için ad; Microsoft Store paketlerinde aynı adlı iki kartı ayırt etmek için kaynak etiketi eklenir.</summary>
    public string AccessibleLabel =>
        string.Equals(Source, PackageSources.MicrosoftStore, StringComparison.OrdinalIgnoreCase)
            ? $"{Name} ({Loc.Instance["Source_MicrosoftStore"]})"
            : Name;
}

/// <summary>Paket kaynaklarının (winget katalog adlarının) kararlı adları.</summary>
public static class PackageSources
{
    public const string Winget = "winget";
    public const string MicrosoftStore = "msstore";
}

public enum OperationKind { Install, Upgrade, Uninstall }

public enum OperationStatus { Queued, Running, Succeeded, Failed, Cancelled, NotFound }

/// <summary>
/// İşlem sonucu. <see cref="Message"/> dilden bağımsız teknik ayrıntıdır (günlük ve ipucu için), kullanıcı metni değildir.
/// <see cref="ReasonKey"/> başarısız/iptal/bulunamadı sonucunun kullanıcıya gösterilen nedenini anlatan <see cref="OperationReason"/>
/// anahtarıdır (<c>Strings.resx</c>); yoksa durumdan türetilir.
/// </summary>
public sealed record OperationOutcome(OperationStatus Status, string? Message, string? ReasonKey = null);

/// <summary>Bitmiş bir işin neden başarısız/iptal/bulunamadı olduğunu anlatan metin anahtarları (<c>Strings.resx</c>).</summary>
public static class OperationReason
{
    public const string NotFound = "Reason_NotFound";
    public const string CatalogUnavailable = "Reason_CatalogUnavailable";
    public const string InstallFailed = "Reason_InstallFailed";
    public const string UpgradeFailed = "Reason_UpgradeFailed";
    public const string UpgradeNotNeeded = "Reason_UpgradeNotNeeded";
    public const string UninstallFailed = "Reason_UninstallFailed";
    public const string UserCancelled = "Reason_UserCancelled";
    public const string Unexpected = "Reason_Unexpected";
}

/// <summary>
/// İlerleme bildirimi. <see cref="Percent"/> 0-100 arasıdır, bilinmiyorsa <c>null</c>.
/// <see cref="Stage"/> <see cref="ProgressStage"/> sabitlerinden biridir (arayüz bunları çevirir).
/// </summary>
public sealed record OperationProgress(double? Percent, string Stage);

/// <summary><see cref="OperationProgress.Stage"/> için kararlı anahtarlar.</summary>
public static class ProgressStage
{
    public const string Queued = "queued";
    public const string Downloading = "downloading";
    public const string Installing = "installing";
    public const string PostInstall = "postInstall";
    public const string Uninstalling = "uninstalling";
    public const string PostUninstall = "postUninstall";
    public const string Finished = "finished";
}
