namespace KolayKur.Core.Health;

public sealed record ProbeResult(bool Succeeded, string Output, string? Error);

public sealed record PackageDependency(string Name, Version MinVersion);

/// <summary>Winget teşhisinin ihtiyaç duyduğu sistem okumaları; testlerde sahtesi kullanılır.</summary>
public interface ISystemProbe
{
    Task<ProbeResult> RunWingetVersionAsync(TimeSpan timeout, CancellationToken ct);

    Version? GetAppInstallerVersion();

    IReadOnlyList<PackageDependency> GetAppInstallerDependencies();

    Version? GetRegisteredVersion(string packageName);
}

/// <summary>App Installer paketinin AppxManifest.xml dosyası yok, okunamıyor veya bozuk.</summary>
public sealed class PackageManifestUnreadableException : Exception
{
    public PackageManifestUnreadableException(string message)
        : base(message)
    {
    }

    public PackageManifestUnreadableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
