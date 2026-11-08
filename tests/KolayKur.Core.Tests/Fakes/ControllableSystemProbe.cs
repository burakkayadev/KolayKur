using KolayKur.Core.Health;

namespace KolayKur.Core.Tests.Fakes;

/// <summary>
/// Testin ortasında durumu değişebilen winget yoklaması: <see cref="Working"/> true ise sağlıklı, değilse "App Installer
/// yok" durumudur; <see cref="Failure"/> doluysa yoklama bu istisnayı fırlatır (örn. bozuk AppX durumu).
/// </summary>
public sealed class ControllableSystemProbe : ISystemProbe
{
    public volatile bool Working;

    public volatile Exception? Failure;

    public Task<ProbeResult> RunWingetVersionAsync(TimeSpan timeout, CancellationToken ct) =>
        Failure is { } failure
            ? Task.FromException<ProbeResult>(failure)
            : Task.FromResult(Working ? new ProbeResult(true, "v1.29.380", null) : new ProbeResult(false, "", "winget yok"));

    public Version? GetAppInstallerVersion() => null;

    public IReadOnlyList<PackageDependency> GetAppInstallerDependencies() => [];

    public Version? GetRegisteredVersion(string packageName) => null;
}
