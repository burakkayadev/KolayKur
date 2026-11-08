namespace KolayKur.Core.Repair;

public sealed record RepairFile(string Name, Uri Url, long Size, string Sha256);

public sealed record WingetRelease(string Tag, RepairFile AppInstaller, RepairFile Dependencies);

public interface IRepairActions
{
    Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct);

    Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct);

    IReadOnlyList<string> ExtractDependencies(string zipPath);

    Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct);

    Task RegisterAppInstallerAsync(CancellationToken ct);
}
