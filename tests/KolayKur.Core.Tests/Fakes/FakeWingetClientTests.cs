using KolayKur.Core.Models;

namespace KolayKur.Core.Tests.Fakes;

public class FakeWingetClientTests
{
    private static readonly IProgress<OperationProgress> NoProgress = new Progress<OperationProgress>();

    [Fact]
    public async Task Per_package_outcome_overrides_default_and_ignores_case()
    {
        var fake = new FakeWingetClient();
        fake.SetOutcome("Some.App", new OperationOutcome(OperationStatus.Failed, "boom"));

        var failed = await fake.InstallAsync("some.app", NoProgress, CancellationToken.None);
        var succeeded = await fake.InstallAsync("Other.App", NoProgress, CancellationToken.None);

        Assert.Equal(OperationStatus.Failed, failed.Status);
        Assert.Equal(OperationStatus.Succeeded, succeeded.Status);
        Assert.Equal(
            [new FakeCall(OperationKind.Install, "some.app"), new FakeCall(OperationKind.Install, "Other.App")],
            fake.Calls);
    }

    [Fact]
    public async Task Held_package_waits_until_released()
    {
        var fake = new FakeWingetClient();
        var gate = fake.Hold("Slow.App");

        var task = fake.UninstallAsync("Slow.App", NoProgress, CancellationToken.None);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(task.IsCompleted);
        gate.Release();
        Assert.Equal(OperationStatus.Succeeded, (await task.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(1, fake.MaxConcurrent);
    }

    [Fact]
    public async Task Held_package_honors_cancellation_and_reports_progress()
    {
        var fake = new FakeWingetClient();
        var gate = fake.Hold("Slow.App");
        var reports = new List<OperationProgress>();
        using var cts = new CancellationTokenSource();

        var task = fake.InstallAsync("Slow.App", new SyncProgress(reports.Add), cts.Token);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Single(reports);
    }

    private sealed class SyncProgress(Action<OperationProgress> report) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => report(value);
    }
}
