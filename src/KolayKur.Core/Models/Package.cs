namespace KolayKur.Core.Models;

/// <summary>Bir paketin kataloglardan birleştirilmiş görünümü. Sürümler yoksa <c>null</c>.</summary>
public sealed record PackageInfo(
    string Id, string Name, string Publisher, string? InstalledVersion, string? AvailableVersion);

public enum OperationKind { Install, Upgrade, Uninstall }

public enum OperationStatus { Queued, Running, Succeeded, Failed, Cancelled, NotFound }

/// <summary>İşlem sonucu. <see cref="Message"/> teknik ayrıntıdır (günlük için), kullanıcı metni değildir.</summary>
public sealed record OperationOutcome(OperationStatus Status, string? Message);

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
