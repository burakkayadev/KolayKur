using System.Globalization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;

namespace KolayKur.Core.Localization;

/// <summary>
/// İşlem listesinin gösterdiği metinlerin anahtarları ve çevrilmiş halleri. Arayüz bunları dönüştürücüler
/// üzerinden kullanır; böylece anahtar kuralları testlenebilir kalır.
/// </summary>
public static class OperationTexts
{
    private static readonly string[] KnownStages =
    [
        ProgressStage.Queued,
        ProgressStage.Downloading,
        ProgressStage.Installing,
        ProgressStage.PostInstall,
        ProgressStage.Uninstalling,
        ProgressStage.PostUninstall,
        ProgressStage.Finished,
    ];

    public static string StatusKey(OperationStatus status) => $"Operation_Status_{status}";

    public static string KindKey(OperationKind kind) => $"Operation_Kind_{kind}";

    public static string StageKey(string stage) => $"Stage_{stage}";

    /// <summary>
    /// Bitmiş işin neden metni anahtarı: açıkça verilmişse o, yoksa duruma göre varsayılan; başarı ve bitmemiş durumlarda <c>null</c>.
    /// </summary>
    public static string? ReasonKeyFor(OperationStatus status, string? reasonKey) => status switch
    {
        OperationStatus.Failed or OperationStatus.Cancelled or OperationStatus.NotFound when reasonKey is not null => reasonKey,
        OperationStatus.Failed => OperationReason.Unexpected,
        OperationStatus.Cancelled => OperationReason.UserCancelled,
        OperationStatus.NotFound => OperationReason.NotFound,
        _ => null,
    };

    /// <summary>
    /// İşin yanında gösterilen ayrıntı, geçerli dilde. Çalışırken <paramref name="message"/> bir <see cref="ProgressStage"/>
    /// anahtarıdır ve çevrilir; bittiğinde sonucun nedeni gösterilir. Ham teknik ayrıntı (hata kodları) burada asla
    /// gösterilmez, yalnızca günlükte ve ipucunda yer alır.
    /// </summary>
    public static string DetailText(OperationStatus status, string? message, string? reasonKey)
    {
        if (status == OperationStatus.Running)
        {
            return message is not null && Array.Exists(KnownStages, stage => string.Equals(stage, message, StringComparison.Ordinal))
                ? Loc.Instance[StageKey(message)]
                : string.Empty;
        }

        return ReasonKeyFor(status, reasonKey) is { } key ? Loc.Instance[key] : string.Empty;
    }

    /// <summary>Toplu işin özeti; sayılar kültürden bağımsız yazılır.</summary>
    public static string SummaryText(BatchSummary summary) =>
        string.Format(
            CultureInfo.InvariantCulture,
            Loc.Instance["OperationList_Summary"],
            summary.Succeeded,
            summary.Failed,
            summary.Cancelled,
            summary.NotFound);
}
