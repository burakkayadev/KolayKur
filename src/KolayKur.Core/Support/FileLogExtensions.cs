using System.Diagnostics;

namespace KolayKur.Core.Support;

public static class FileLogExtensions
{
    /// <summary>Günlük yazılamaması (disk dolu vb.) ekranı çökertmesin; hata en azından izleme çıktısına gider.</summary>
    public static void TryError(this FileLog? log, string message, Exception? ex = null)
    {
        try
        {
            log?.Error(message, ex);
        }
        catch (Exception logEx)
        {
            Trace.TraceError($"KolayKur günlüğüne yazılamadı: {logEx}");
        }
    }

    public static void TryInfo(this FileLog? log, string message)
    {
        try
        {
            log?.Info(message);
        }
        catch (Exception logEx)
        {
            Trace.TraceError($"KolayKur günlüğüne yazılamadı: {logEx}");
        }
    }

    /// <summary>
    /// Beklenmeyen bir hatayı hem izleme çıktısına (hata ayıklama, günlük yoksa tek iz) hem de günlük dosyasına yazar.
    /// Yutulmaz: çağıran ayrıca kullanıcıya gösterilecek hata durumunu ayarlar.
    /// </summary>
    public static void ReportUnexpected(this FileLog? log, string message, Exception ex)
    {
        Trace.TraceError($"KolayKur: {message} {ex}");
        log.TryError(message, ex);
    }
}
