using KolayKur.Core.Support;

namespace KolayKur.Core.Winget;

/// <summary>
/// "Önce winget ile Microsoft Store birlikte, olmazsa yalnız winget" sırası. Store'a ulaşılamaması kullanıcıya
/// hata olarak yansımaz (günlüğe bir kayıt düşer); winget kataloğunun kendisinin hatası ise ikinci denemeden
/// aynen yükselir.
/// </summary>
internal static class StoreFallback
{
    /// <param name="attempt">Bağlanıp işi yapar; parametre Store kataloğunun dahil edilip edilmeyeceğidir.</param>
    /// <param name="log">Yedek dala geçildiğinde bir kayıt düşer.</param>
    /// <param name="ct">İptal; iptal hatası yedek dala yönlendirilmez.</param>
    internal static async Task<T> RunAsync<T>(Func<bool, Task<T>> attempt, FileLog? log, CancellationToken ct)
    {
        try
        {
            return await attempt(true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.TryError("Microsoft Store kataloğu kullanılamadı; yalnız winget ile devam ediliyor.", ex);
        }

        ct.ThrowIfCancellationRequested();
        return await attempt(false).ConfigureAwait(false);
    }
}
