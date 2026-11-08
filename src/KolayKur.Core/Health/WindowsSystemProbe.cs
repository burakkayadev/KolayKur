using System.Globalization;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace KolayKur.Core.Health;

/// <summary>
/// Gerçek sistemi okur. Paketleri <see cref="PackageManager"/> ile (normal kullanıcıyla çalışır) listeler.
/// Bağımlılıklar paket klasöründeki AppxManifest.xml dosyasından alınır; arızalı bilgisayarda
/// <c>Package.Dependencies</c> değeri boş geldiği için ona güvenilmez.
/// </summary>
public sealed class WindowsSystemProbe : ISystemProbe
{
    private const string AppInstallerName = "Microsoft.DesktopAppInstaller";
    private const string WingetFile = "winget";
    private const string WingetArguments = "--version";

    public Task<ProbeResult> RunWingetVersionAsync(TimeSpan timeout, CancellationToken ct) =>
        ProcessRunner.RunAsync(WingetFile, WingetArguments, timeout, ct);

    public Version? GetAppInstallerVersion() =>
        FindLatest(AppInstallerName)?.Version;

    public IReadOnlyList<PackageDependency> GetAppInstallerDependencies()
    {
        var path = FindLatest(AppInstallerName)?.InstalledPath;
        return path is null ? [] : ReadDependencies(Path.Combine(path, "AppxManifest.xml"));
    }

    public Version? GetRegisteredVersion(string packageName) =>
        FindLatest(packageName)?.Version;

    private static (Version Version, string InstalledPath)? FindLatest(string packageName)
    {
        var packages = new PackageManager()
            .FindPackagesForUserWithPackageTypes(string.Empty, PackageTypes.Main | PackageTypes.Framework);

        (Version Version, string InstalledPath)? latest = null;
        foreach (var package in packages)
        {
            if (!string.Equals(package.Id.Name, packageName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var version = ToVersion(package.Id.Version);
            if (latest is null || version > latest.Value.Version)
            {
                latest = (version, package.InstalledPath);
            }
        }

        return latest;
    }

    private static Version ToVersion(PackageVersion v) => new(v.Major, v.Minor, v.Build, v.Revision);

    private static IReadOnlyList<PackageDependency> ReadDependencies(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var manifest = XDocument.Load(manifestPath);
        var dependencies = new List<PackageDependency>();
        foreach (var element in manifest.Descendants().Where(e => e.Name.LocalName == "PackageDependency"))
        {
            var name = element.Attribute("Name")?.Value;
            var minVersion = element.Attribute("MinVersion")?.Value;
            if (name is not null && Version.TryParse(minVersion, out var parsed))
            {
                dependencies.Add(new PackageDependency(name, parsed));
            }
        }

        return dependencies;
    }
}
