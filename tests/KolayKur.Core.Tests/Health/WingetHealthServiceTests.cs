using System.Diagnostics;
using KolayKur.Core.Health;
using KolayKur.Core.Tests.Fakes;

namespace KolayKur.Core.Tests.Health;

public class WingetHealthServiceTests
{
    private const string WindowsAppRuntime = "Microsoft.WindowsAppRuntime.1.8";

    private static readonly ProbeResult WingetFailed =
        new(false, "", "Sistem dosyaya erişemiyor");

    private static readonly Version AppInstaller = new(1, 29, 379, 0);

    private static readonly PackageDependency[] RuntimeDependency =
        [new(WindowsAppRuntime, new Version(8000, 616, 304, 0))];

    private static Task<WingetDiagnosis> DiagnoseAsync(FakeSystemProbe probe) =>
        new WingetHealthService(probe).DiagnoseAsync(CancellationToken.None);

    [Fact]
    public async Task Healthy_when_winget_runs()
    {
        var probe = new FakeSystemProbe { WingetResult = new ProbeResult(true, "v1.29.380", null) };

        var diagnosis = await DiagnoseAsync(probe);

        Assert.Equal(WingetState.Healthy, diagnosis.State);
        Assert.Equal("v1.29.380", diagnosis.Detail);
    }

    [Fact]
    public async Task NotInstalled_when_app_installer_missing()
    {
        var probe = new FakeSystemProbe { WingetResult = WingetFailed, AppInstallerVersion = null };

        var diagnosis = await DiagnoseAsync(probe);

        Assert.Equal(WingetState.NotInstalled, diagnosis.State);
    }

    [Fact]
    public async Task Paused_framework_is_dependency_broken()
    {
        var probe = new FakeSystemProbe
        {
            WingetResult = WingetFailed,
            AppInstallerVersion = AppInstaller,
            Dependencies = RuntimeDependency,
        };

        var diagnosis = await DiagnoseAsync(probe);

        Assert.Equal(WingetState.DependencyBroken, diagnosis.State);
        Assert.Equal(WindowsAppRuntime, diagnosis.BrokenDependency);
    }

    [Fact]
    public async Task Old_framework_is_dependency_broken()
    {
        var probe = new FakeSystemProbe
        {
            WingetResult = WingetFailed,
            AppInstallerVersion = AppInstaller,
            Dependencies = RuntimeDependency,
            Registered = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
            {
                [WindowsAppRuntime] = new Version(8000, 500, 0, 0),
            },
        };

        var diagnosis = await DiagnoseAsync(probe);

        Assert.Equal(WingetState.DependencyBroken, diagnosis.State);
        Assert.Equal(WindowsAppRuntime, diagnosis.BrokenDependency);
    }

    [Fact]
    public async Task RegistrationBroken_when_dependencies_ok()
    {
        var probe = new FakeSystemProbe
        {
            WingetResult = WingetFailed,
            AppInstallerVersion = AppInstaller,
            Dependencies = RuntimeDependency,
            Registered = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
            {
                [WindowsAppRuntime] = new Version(8000, 616, 304, 0),
            },
        };

        var diagnosis = await DiagnoseAsync(probe);

        Assert.Equal(WingetState.RegistrationBroken, diagnosis.State);
        Assert.Null(diagnosis.BrokenDependency);
    }

    [Fact]
    public async Task Unreadable_manifest_is_not_installed()
    {
        var probe = new FakeSystemProbe
        {
            WingetResult = WingetFailed,
            AppInstallerVersion = AppInstaller,
            DependenciesException = new PackageManifestUnreadableException("AppxManifest.xml bulunamadı"),
        };

        var diagnosis = await DiagnoseAsync(probe);

        Assert.Equal(WingetState.NotInstalled, diagnosis.State);
        Assert.Null(diagnosis.BrokenDependency);
        Assert.False(string.IsNullOrWhiteSpace(diagnosis.Detail));
        Assert.Contains("AppxManifest.xml bulunamadı", diagnosis.Detail);
    }

    [Fact]
    public async Task Other_probe_exceptions_are_not_swallowed()
    {
        var probe = new FakeSystemProbe
        {
            WingetResult = WingetFailed,
            AppInstallerVersion = AppInstaller,
            DependenciesException = new InvalidOperationException("beklenmeyen"),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => DiagnoseAsync(probe));
    }

    [Fact]
    public void Unparsable_min_version_is_kept_as_zero()
    {
        const string manifest = """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Dependencies>
                <PackageDependency Name="Good.Dep" MinVersion="1.2.3.4" />
                <PackageDependency Name="Bad.Version" MinVersion="abc" />
                <PackageDependency Name="No.Version" />
              </Dependencies>
            </Package>
            """;

        var dependencies = WindowsSystemProbe.ParseDependencies(manifest);

        Assert.Equal(
            [
                new PackageDependency("Good.Dep", new Version(1, 2, 3, 4)),
                new PackageDependency("Bad.Version", new Version(0, 0)),
                new PackageDependency("No.Version", new Version(0, 0)),
            ],
            dependencies);
    }

    [Fact]
    public void Corrupt_manifest_throws_xml_exception()
    {
        Assert.ThrowsAny<System.Xml.XmlException>(() => WindowsSystemProbe.ParseDependencies("<Package><oops"));
    }

    [Fact]
    public async Task Hung_process_times_out()
    {
        var stopwatch = Stopwatch.StartNew();

        var result = await ProcessRunner.RunAsync(
            "ping", "-n 30 127.0.0.1", TimeSpan.FromSeconds(1), CancellationToken.None);

        // Amaç: ping'in 30 sn'sinden çok daha kısa sürede, süre dolunca sonlanması; CI yavaşlığına dayanıklı üst sınır.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15));
        Assert.False(result.Succeeded);
        Assert.Equal("timeout", result.Error);
    }
}
