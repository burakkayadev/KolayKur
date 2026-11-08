using KolayKur.Core.Health;

namespace KolayKur.Core.Tests.Health;

public sealed class WindowsSystemProbeTests : IDisposable
{
    private readonly string localAppData =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(localAppData))
        {
            Directory.Delete(localAppData, recursive: true);
        }
    }

    private sealed class RecordingRunner
    {
        public List<string> Files { get; } = [];

        public Task<ProbeResult> RunAsync(string file, string arguments, TimeSpan timeout, CancellationToken ct)
        {
            Files.Add(file);
            return Task.FromResult(new ProbeResult(true, "v1.29.380", null));
        }
    }

    [Fact]
    public void Locator_builds_full_path_under_the_given_local_app_data()
    {
        var path = WingetLocator.GetPath(localAppData);

        Assert.Equal(Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe"), path);
    }

    [Fact]
    public void Locator_defaults_to_the_current_users_local_app_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");

        Assert.Equal(expected, WingetLocator.GetPath());
        Assert.True(Path.IsPathRooted(WingetLocator.GetPath()));
    }

    [Fact]
    public async Task Probe_runs_winget_by_its_full_path_never_a_bare_name()
    {
        var wingetPath = WingetLocator.GetPath(localAppData);
        Directory.CreateDirectory(Path.GetDirectoryName(wingetPath)!);
        File.WriteAllBytes(wingetPath, []);
        var runner = new RecordingRunner();
        var probe = new WindowsSystemProbe(wingetPath, runner.RunAsync);

        var result = await probe.RunWingetVersionAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.Succeeded);
        var file = Assert.Single(runner.Files);
        Assert.Equal(wingetPath, file);
        Assert.True(Path.IsPathRooted(file));
    }

    [Fact]
    public async Task Missing_winget_file_is_reported_as_not_available_without_starting_anything()
    {
        var runner = new RecordingRunner();
        var probe = new WindowsSystemProbe(WingetLocator.GetPath(localAppData), runner.RunAsync);

        var result = await probe.RunWingetVersionAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Files);
        Assert.Contains("winget.exe", result.Error, StringComparison.Ordinal);
    }
}
