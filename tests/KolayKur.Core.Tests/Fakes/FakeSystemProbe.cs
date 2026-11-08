using KolayKur.Core.Health;

namespace KolayKur.Core.Tests.Fakes;

public sealed class FakeSystemProbe : ISystemProbe
{
    public ProbeResult WingetResult { get; init; } = new(true, "v1.29.380", null);

    public Version? AppInstallerVersion { get; init; }

    public IReadOnlyList<PackageDependency> Dependencies { get; init; } = [];

    public Exception? DependenciesException { get; init; }

    public IReadOnlyDictionary<string, Version> Registered { get; init; } =
        new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);

    public Task<ProbeResult> RunWingetVersionAsync(TimeSpan timeout, CancellationToken ct) =>
        Task.FromResult(WingetResult);

    public Version? GetAppInstallerVersion() => AppInstallerVersion;

    public IReadOnlyList<PackageDependency> GetAppInstallerDependencies() =>
        DependenciesException is null ? Dependencies : throw DependenciesException;

    public Version? GetRegisteredVersion(string packageName) =>
        Registered.TryGetValue(packageName, out var version) ? version : null;
}
