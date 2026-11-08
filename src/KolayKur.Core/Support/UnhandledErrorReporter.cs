using System.Diagnostics;
using KolayKur.Core.Localization;

namespace KolayKur.Core.Support;

/// <summary>
/// Hiçbir yerde yakalanmamış hataların son durağı. Hata her zaman günlüğe (ve izleme çıktısına) yazılır, yutulmaz.
/// Arayüz iş parçacığındaki hatada ayrıca kullanıcıya genel bir mesaj gösterilir; mesaj açıkken oluşan başka hatalar
/// yalnızca günlüğe gider (üst üste diyalog açılmasın). Bu sınıfın hiçbir yöntemi istisna fırlatmaz.
/// </summary>
public sealed class UnhandledErrorReporter(FileLog? log, IDialogService dialogs)
{
    private int dialogOpen;

    /// <summary>Hatayı günlüğe yazar ve kullanıcıya <c>Error_Unhandled</c> metnini gösterir (açık bir diyalog yoksa).</summary>
    public void ReportAndInform(string source, Exception ex)
    {
        WriteLog(source, ex);
        if (Interlocked.Exchange(ref dialogOpen, 1) == 1)
        {
            return;
        }

        try
        {
            dialogs.Inform(Loc.Instance["Error_Unhandled"]);
        }
        catch (Exception dialogEx)
        {
            // Hata penceresi bile açılamıyorsa yapılacak tek şey bunu da kaydetmektir.
            WriteLog("Hata penceresi gösterilemedi", dialogEx);
        }
        finally
        {
            Interlocked.Exchange(ref dialogOpen, 0);
        }
    }

    /// <summary>Hatayı yalnızca günlüğe yazar (arka plan iş parçacıkları; kullanıcıya gösterilecek güvenli bir yer yok).</summary>
    public void ReportOnly(string source, object? exceptionObject)
    {
        if (exceptionObject is Exception ex)
        {
            WriteLog(source, ex);
        }
        else
        {
            WriteLog(source, null, exceptionObject?.ToString());
        }
    }

    private void WriteLog(string source, Exception? ex, string? detail = null)
    {
        var message = detail is null
            ? $"Yakalanmamış hata ({source})."
            : $"Yakalanmamış hata ({source}): {detail}";
        Trace.TraceError($"KolayKur: {message} {ex}");
        log.TryError(message, ex);
    }
}
