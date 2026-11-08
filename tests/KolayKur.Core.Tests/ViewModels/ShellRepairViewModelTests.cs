using KolayKur.Core.Health;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;

namespace KolayKur.Core.Tests.ViewModels;

public sealed class ShellRepairViewModelTests : IDisposable
{
    private static readonly WingetDiagnosis NotInstalled = new(WingetState.NotInstalled, null, "yok");

    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    private FileLog Log => new(logDirectory);

    private RepairViewModel CreateViewModel(IRepairActions actions, ISystemProbe probe, WingetDiagnosis diagnosis)
    {
        var service = new WingetRepairService(actions, new WingetHealthService(probe), Log);
        return new RepairViewModel(service, diagnosis, Log);
    }

    [Fact]
    public async Task Repair_success_raises_repaired()
    {
        // Arrange: onarımdan sonra winget çalışıyor, yeni teşhis Healthy döner.
        var viewModel = CreateViewModel(new FakeRepairActions(), new FakeSystemProbe(), NotInstalled);
        var repaired = 0;
        viewModel.Repaired += (_, _) => repaired++;

        // Act
        await viewModel.RepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(1, repaired);
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Repair_failure_shows_error()
    {
        // Arrange: onarım bitiyor ama yeni teşhis hâlâ DependencyBroken.
        var stillBroken = new FakeSystemProbe
        {
            WingetResult = new ProbeResult(false, "", "çalışmıyor"),
            AppInstallerVersion = new Version(1, 29, 379, 0),
            Dependencies = [new PackageDependency(FakeRepairActions.WindowsAppRuntime, new Version(8000, 616, 304, 0))],
        };
        var viewModel = CreateViewModel(
            new FakeRepairActions(), stillBroken, new WingetDiagnosis(WingetState.RegistrationBroken, null, "kayıt"));
        var repaired = 0;
        viewModel.Repaired += (_, _) => repaired++;

        // Act
        await viewModel.RepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Repair_Failed", viewModel.ErrorKey);
        Assert.Equal(0, repaired);
    }

    [Fact]
    public async Task Corrupt_download_shows_verification_error()
    {
        var actions = new ThrowingActions(new DownloadVerificationException("özet tutmuyor"));
        var viewModel = CreateViewModel(actions, new FakeSystemProbe(), NotInstalled);
        var repaired = 0;
        viewModel.Repaired += (_, _) => repaired++;

        await viewModel.RepairCommand.ExecuteAsync(null);

        Assert.Equal("Repair_VerificationFailed", viewModel.ErrorKey);
        Assert.Equal(0, repaired);
    }

    [Fact]
    public async Task Unexpected_exception_shows_failed_error_and_is_logged()
    {
        var actions = new ThrowingActions(new InvalidOperationException("beklenmeyen-hata-metni"));
        var viewModel = CreateViewModel(actions, new FakeSystemProbe(), NotInstalled);

        await viewModel.RepairCommand.ExecuteAsync(null);

        Assert.Equal("Repair_Failed", viewModel.ErrorKey);
        Assert.Contains("beklenmeyen-hata-metni", File.ReadAllText(Log.CurrentFile));
    }

    [Fact]
    public async Task New_attempt_clears_previous_error()
    {
        var actions = new ThrowingActions(new InvalidOperationException("ilk deneme"));
        var viewModel = CreateViewModel(actions, new FakeSystemProbe(), NotInstalled);
        await viewModel.RepairCommand.ExecuteAsync(null);
        Assert.Equal("Repair_Failed", viewModel.ErrorKey);

        actions.Failure = null;
        await viewModel.RepairCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Repair_does_not_block_the_calling_thread()
    {
        var actions = new BlockingActions();
        var viewModel = CreateViewModel(actions, new FakeSystemProbe(), NotInstalled);
        var callerThread = Environment.CurrentManagedThreadId;

        var running = viewModel.RepairCommand.ExecuteAsync(null);
        await actions.Entered.WaitAsync(TimeSpan.FromSeconds(10));

        // Çağıran iş parçacığı dönebildi ve onarım başka bir iş parçacığında bekliyor.
        Assert.NotEqual(callerThread, actions.ThreadId);
        Assert.False(running.IsCompleted);

        actions.Release();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class ThrowingActions(Exception failure) : IRepairActions
    {
        public Exception? Failure { get; set; } = failure;

        private readonly FakeRepairActions inner = new();

        public Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct) =>
            Failure is null ? inner.GetLatestReleaseAsync(ct) : Task.FromException<WingetRelease>(Failure);

        public Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct) =>
            inner.DownloadAsync(file, progress, ct);

        public IReadOnlyList<string> ExtractDependencies(string zipPath) => inner.ExtractDependencies(zipPath);

        public Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct) =>
            inner.AddPackageAsync(packagePath, dependencyPaths, ct);

        public Task RegisterAppInstallerAsync(CancellationToken ct) => inner.RegisterAppInstallerAsync(ct);
    }

    /// <summary>İlk adımda eşzamanlı (iş parçacığını tutan) bekleyen onarım eylemleri.</summary>
    private sealed class BlockingActions : IRepairActions
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim released = new();
        private readonly FakeRepairActions inner = new();

        public Task Entered => entered.Task;

        public int ThreadId { get; private set; }

        public void Release() => released.Set();

        public Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            entered.TrySetResult();
            // Sınırsız beklemek, onarım çağıranı bloklarsa testin sonsuza dek asılı kalmasına yol açardı.
            released.Wait(TimeSpan.FromSeconds(15), ct);
            return inner.GetLatestReleaseAsync(ct);
        }

        public Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct) =>
            inner.DownloadAsync(file, progress, ct);

        public IReadOnlyList<string> ExtractDependencies(string zipPath) => inner.ExtractDependencies(zipPath);

        public Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct) =>
            inner.AddPackageAsync(packagePath, dependencyPaths, ct);

        public Task RegisterAppInstallerAsync(CancellationToken ct) => inner.RegisterAppInstallerAsync(ct);
    }
}
