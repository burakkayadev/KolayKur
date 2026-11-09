using KolayKur.Core.Icons;
using KolayKur.Core.Models;

namespace KolayKur.Core.Winget;

/// <summary>
/// winget ile konuşan istemci. İptal (<c>ct</c>) <see cref="OperationCanceledException"/> olarak yansır.
/// Kataloğa bağlanılamazsa <see cref="CatalogUnavailableException"/> atılır.
/// İlerleme bildirimleri iş parçacığı havuzunda/COM geri çağrı iş parçacığında gelir; arayüze dokunan tüketiciler
/// (örn. işlem sırası) kendileri doğru iş parçacığına geçirmelidir.
/// </summary>
public interface IWingetClient
{
    Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct);

    Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct);

    /// <summary>
    /// Paketin simgesini bulmaya yarayan ipuçlarını (katalog simgeleri, site, kurulu program simgesi) okur; salt okur.
    /// Paket bulunamazsa <see cref="IconHints.Empty"/> döner.
    /// </summary>
    Task<IconHints> GetIconHintsAsync(string packageId, string? source, CancellationToken ct);

    Task<OperationOutcome> InstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);

    Task<OperationOutcome> UpgradeAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);

    Task<OperationOutcome> UninstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);
}

public sealed class CatalogUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
