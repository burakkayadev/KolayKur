using KolayKur.Core.Repair;

namespace KolayKur.Core.Tests.Fakes;

public sealed class FakeRepairActions : IRepairActions
{
    public const string WindowsAppRuntime = "Microsoft.WindowsAppRuntime.1.8";

    private const string AppInstallerName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    private const string DependenciesName = "DesktopAppInstaller_Dependencies.zip";

    public static readonly string[] ExtractedPackages =
    [
        @"C:\fake\x64\Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_x64__8wekyb3d8bbwe.appx",
        @"C:\fake\x64\Microsoft.UI.Xaml.2.8_8.2310.30001.0_x64__8wekyb3d8bbwe.appx",
        @"C:\fake\x64\Microsoft.WindowsAppRuntime.1.8_8000.616.304.0_x64__8wekyb3d8bbwe.appx",
    ];

    public List<string> Calls { get; } = [];

    public int CleanupCount { get; private set; }

    public Exception? CleanupException { get; set; }

    public List<IReadOnlyList<string>> AddedDependencies { get; } = [];

    public async Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct)
    {
        Calls.Add("Release");
        await Task.Yield();
        return new WingetRelease(
            "v1.29.380",
            new RepairFile(AppInstallerName, new Uri("https://example.test/app"), 1, "aa"),
            new RepairFile(DependenciesName, new Uri("https://example.test/deps"), 1, "bb"));
    }

    public async Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct)
    {
        Calls.Add(file.Name == AppInstallerName ? "Download:AppInstaller" : "Download:Dependencies");
        await Task.Yield();
        return @"C:\fake\" + file.Name;
    }

    public IReadOnlyList<string> ExtractDependencies(string zipPath)
    {
        Calls.Add("Extract");
        return ExtractedPackages;
    }

    public async Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct)
    {
        var fileName = Path.GetFileName(packagePath);
        Calls.Add(fileName == AppInstallerName
            ? "Add:AppInstaller"
            : "Add:" + fileName[..fileName.IndexOf('_')]);
        AddedDependencies.Add(dependencyPaths);
        await Task.Yield();
    }

    public async Task RegisterAppInstallerAsync(CancellationToken ct)
    {
        Calls.Add("Register");
        await Task.Yield();
    }

    public void CleanupDownloads()
    {
        CleanupCount++;
        if (CleanupException is not null)
        {
            throw CleanupException;
        }
    }
}
