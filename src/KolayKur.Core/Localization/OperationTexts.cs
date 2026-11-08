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
    /// İşin yanında gösterilen ayrıntı. Çalışırken <see cref="OperationItem.Message"/> bir
    /// <see cref="ProgressStage"/> anahtarıdır ve çevrilir; bittiğinde sonucun teknik ayrıntısıdır ve olduğu gibi gösterilir.
    /// </summary>
    public static string MessageText(OperationStatus status, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var isStage = status == OperationStatus.Running
            && Array.Exists(KnownStages, stage => string.Equals(stage, message, StringComparison.Ordinal));
        return isStage ? Loc.Instance[StageKey(message)] : message;
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
