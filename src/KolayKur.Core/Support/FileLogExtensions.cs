using System.Diagnostics;

namespace KolayKur.Core.Support;

internal static class FileLogExtensions
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
}
