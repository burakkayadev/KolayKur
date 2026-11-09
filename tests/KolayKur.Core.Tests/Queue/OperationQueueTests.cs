using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Queue;

public sealed class OperationQueueTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Runs_one_at_a_time_in_order()
    {
        // Arrange
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var queue = CreateQueue(client);

        // Act
        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Upgrade, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Uninstall, Pkg("C.Three"));
        await gate.Started.WaitAsync(Timeout);

        // Assert: while the first one is held, nothing else has started.
        Assert.Equal([new FakeCall(OperationKind.Install, "A.One")], client.Calls);

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(
            [
                new FakeCall(OperationKind.Install, "A.One"),
                new FakeCall(OperationKind.Upgrade, "B.Two"),
                new FakeCall(OperationKind.Uninstall, "C.Three"),
            ],
            client.Calls);
        Assert.Equal(1, client.MaxConcurrent);
        Assert.All(queue.Items, item => Assert.Equal(OperationStatus.Succeeded, item.Status));
    }

    [Fact]
    public async Task ClearFinished_removes_only_finished_items()
    {
        // Arrange: A bitti, B tutuluyor (Running), C sırada.
        var client = new FakeWingetClient();
        var gate = client.Hold("B.Two");
        var queue = CreateQueue(client);
        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));
        await gate.Started.WaitAsync(Timeout);
        var b = queue.Items[1];
        var c = queue.Items[2];
        Assert.Equal(OperationStatus.Succeeded, queue.Items[0].Status);

        // Act
        var removed = queue.ClearFinished();

        // Assert
        Assert.Equal(1, removed);
        Assert.Equal([b, c], queue.Items);
        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
    }

    [Fact]
    public void ClearFinished_on_empty_queue_returns_zero()
    {
        var queue = CreateQueue(new FakeWingetClient());

        Assert.Equal(0, queue.ClearFinished());
        Assert.Empty(queue.Items);
    }

    [Fact]
    public async Task Finished_package_can_be_enqueued_again_after_clear()
    {
        var queue = CreateQueue(new FakeWingetClient());
        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal(1, queue.ClearFinished());
        Assert.Empty(queue.Items);

        var again = queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.NotNull(again);
        Assert.Equal([again], queue.Items);
        Assert.Equal(OperationStatus.Succeeded, again.Status);
    }

    [Fact]
    public async Task ClearFinished_on_the_queue_context_removes_only_finished_items()
    {
        // Arrange: kuyruk bir bağlamda kurulur; değişiklikler o bağlama taşınır.
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("B.Two");
        var queue = pump.Run(() => CreateQueue(client));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));
        await gate.Started.WaitAsync(Timeout);
        await pump.FlushAsync();

        // Act
        var removed = pump.Run(() => queue.ClearFinished());

        // Assert
        Assert.Equal(1, removed);
        Assert.Equal(["B.Two"], pump.Run(() => queue.Items.Select(item => item.Package.Id).ToList()));
        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task Failed_item_does_not_stop_next()
    {
        var client = new FakeWingetClient();
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.Failed, "kurulum hatası"));
        var queue = CreateQueue(client);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(
            [OperationStatus.Succeeded, OperationStatus.Failed, OperationStatus.Succeeded],
            queue.Items.Select(item => item.Status));
        Assert.Equal("kurulum hatası", queue.Items[1].Message);
        Assert.Equal(OperationReason.Unexpected, queue.Items[1].ReasonKey);
    }

    [Fact]
    public async Task Denied_elevation_cancels_only_that_item()
    {
        var client = new FakeWingetClient();
        client.SetOutcome("A.One", new OperationOutcome(OperationStatus.Cancelled, "yönetici izni verilmedi"));
        var queue = CreateQueue(client);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(
            [OperationStatus.Cancelled, OperationStatus.Succeeded],
            queue.Items.Select(item => item.Status));
        Assert.Contains(new FakeCall(OperationKind.Install, "B.Two"), client.Calls);
    }

    [Fact]
    public async Task Summary_counts_results()
    {
        var client = new FakeWingetClient();
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.Failed, null));
        var queue = CreateQueue(client);
        var completed = new TaskCompletionSource<BatchSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.BatchCompleted += (_, summary) => completed.TrySetResult(summary);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.True(completed.Task.IsCompleted, "BatchCompleted must fire before the queue reports idle.");
        Assert.Equal(new BatchSummary(2, 1, 0, 0), await completed.Task);
    }

    [Fact]
    public async Task Summary_counts_only_the_current_batch()
    {
        var client = new FakeWingetClient();
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.NotFound, null));
        var queue = CreateQueue(client);
        var summaries = new ConcurrentQueue<BatchSummary>();
        queue.BatchCompleted += (_, summary) => summaries.Enqueue(summary);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal([new BatchSummary(1, 0, 0, 0), new BatchSummary(0, 0, 0, 1)], summaries);
    }

    [Fact]
    public async Task Second_click_is_ignored()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var queue = CreateQueue(client);

        var first = queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await gate.Started.WaitAsync(Timeout);
        var second = queue.Enqueue(OperationKind.Install, Pkg("A.One"));

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Single(queue.Items);
        Assert.True(queue.IsBusy);

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Single(client.Calls);
        Assert.False(queue.IsBusy);
    }

    [Fact]
    public async Task Id_compare_ignores_case_in_turkish_culture()
    {
        var original = CultureInfo.CurrentCulture;
        var client = new FakeWingetClient();
        var gate = client.Hold("INTEL.Driver");
        var queue = CreateQueue(client);
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            var first = queue.Enqueue(OperationKind.Install, Pkg("INTEL.Driver"));
            var second = queue.Enqueue(OperationKind.Install, Pkg("intel.driver"));

            Assert.NotNull(first);
            Assert.Null(second);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            gate.Release();
            await queue.WhenIdleAsync().WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task Finished_package_can_run_again()
    {
        var client = new FakeWingetClient();
        var queue = CreateQueue(client);

        var first = queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        var again = queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.NotNull(first);
        Assert.NotNull(again);
        Assert.NotSame(first, again);
        Assert.Equal(2, client.Calls.Count);
        Assert.Equal(2, queue.Items.Count);
    }

    [Fact]
    public async Task Client_exception_marks_item_failed_and_continues()
    {
        var client = new ThrowingClient("B.Two");
        var queue = CreateQueue(client);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(
            [OperationStatus.Succeeded, OperationStatus.Failed, OperationStatus.Succeeded],
            queue.Items.Select(item => item.Status));
        Assert.Equal("winget çöktü", queue.Items[1].Message);
        Assert.Equal(OperationReason.Unexpected, queue.Items[1].ReasonKey);
        Assert.Contains(
            "winget çöktü", File.ReadAllText(new FileLog(logDirectory).CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Observable_changes_run_on_the_captured_synchronization_context()
    {
        using var context = new PumpContext();
        var client = new FakeWingetClient();
        client.SetOutcome("A.One", new OperationOutcome(OperationStatus.Failed, "hata"));
        var queue = context.Run(() => CreateQueue(client));

        var threads = new ConcurrentBag<int>();
        var changedProperties = new ConcurrentBag<string?>();
        var completed = new TaskCompletionSource<BatchSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((INotifyCollectionChanged)queue.Items).CollectionChanged += (_, e) =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            foreach (var added in e.NewItems!.Cast<OperationItem>())
            {
                added.PropertyChanged += (_, args) =>
                {
                    threads.Add(Environment.CurrentManagedThreadId);
                    changedProperties.Add(args.PropertyName);
                };
            }
        };
        queue.BatchCompleted += (_, summary) =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            completed.TrySetResult(summary);
        };

        context.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        await completed.Task.WaitAsync(Timeout);
        await context.FlushAsync();

        Assert.NotEmpty(threads);
        Assert.All(threads, id => Assert.Equal(context.ThreadId, id));
        Assert.Contains(nameof(OperationItem.Status), changedProperties);
        Assert.Equal(OperationStatus.Failed, queue.Items.Single().Status);
    }

    private OperationQueue CreateQueue(IWingetClient client) => new(client, new FileLog(logDirectory));

    private static PackageInfo Pkg(string id) => new(id, id, "Yayıncı", null, "1.0");

    private sealed class ThrowingClient(string failingId) : IWingetClient
    {
        private readonly FakeWingetClient inner = new();

        public Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct) =>
            inner.SearchAsync(query, ct);

        public Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct) =>
            inner.GetInstalledAsync(ct);

        public Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct) =>
            inner.GetUpgradesAsync(ct);

        public Task<OperationOutcome> InstallAsync(
            string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
            string.Equals(packageId, failingId, StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("winget çöktü")
                : inner.InstallAsync(packageId, progress, ct);

        public Task<OperationOutcome> UpgradeAsync(
            string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
            inner.UpgradeAsync(packageId, progress, ct);

        public Task<KolayKur.Core.Icons.IconHints> GetIconHintsAsync(string packageId, string? source, CancellationToken ct) =>
            inner.GetIconHintsAsync(packageId, source, ct);

        public Task<OperationOutcome> UninstallAsync(
            string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
            inner.UninstallAsync(packageId, progress, ct);
    }
}
