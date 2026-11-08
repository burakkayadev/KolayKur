using KolayKur.Core.Models;

namespace KolayKur.Core.Winget;

/// <summary>
/// winget ile konuşan istemci. İptal (<c>ct</c>) <see cref="OperationCanceledException"/> olarak yansır.
/// Kataloğa bağlanılamazsa <see cref="CatalogUnavailableException"/> atılır.
/// </summary>
public interface IWingetClient
{
    Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct);

    Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct);

    Task<OperationOutcome> InstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);

    Task<OperationOutcome> UpgradeAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);

    Task<OperationOutcome> UninstallAsync(string packageId, IProgress<OperationProgress> progress, CancellationToken ct);
}

public sealed class CatalogUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
