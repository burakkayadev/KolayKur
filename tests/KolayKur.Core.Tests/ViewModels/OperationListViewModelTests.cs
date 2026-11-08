using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;

namespace KolayKur.Core.Tests.ViewModels;

// Özet metni Loc.Instance'tan geldiği için dil testleriyle aynı koleksiyonda çalışır.
// Gerçek iş yapan testlerde kuyruk ve ViewModel tek iş parçacıklı PumpContext'te kurulur ve okunur (uygulamadaki
// arayüz iş parçacığı gibi): koleksiyon ve Status değişiklikleri tek iş parçacığında olur, test iş parçacığıyla yarışmaz.
[Collection("Localization")]
public sealed class OperationListViewModelTests : QueueTestBase
{
    private (OperationQueue Queue, OperationListViewModel ViewModel) Create(PumpContext pump, FakeWingetClient client) =>
        pump.Run(() =>
        {
            var queue = CreateQueue(client);
            return (queue, new OperationListViewModel(queue));
        });

    [Fact]
    public async Task Summary_text_counts_results()
    {
        // Arrange: one batch of three operations, the second one fails -> BatchSummary(2, 1, 0, 0).
        Loc.Instance.SetLanguage("tr");
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.Failed, "hata"));
        var (queue, viewModel) = Create(pump, client);
        Assert.Null(pump.Run(() => viewModel.SummaryText));

        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("C.Three")));

        // Act
        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        var summary = pump.Run(() => viewModel.SummaryText);
        Assert.NotNull(summary);
        Assert.Contains("2", summary);
        Assert.Contains("1", summary);
        Assert.Equal("2 başarılı, 1 başarısız, 0 iptal, 0 bulunamadı", summary);
        Assert.Same(queue.Items, viewModel.Items);
    }

    [Fact]
    public async Task Summary_is_cleared_when_a_new_batch_starts()
    {
        // Arrange: ilk toplu iş bitti ve özeti görünüyor.
        Loc.Instance.SetLanguage("tr");
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var (queue, viewModel) = Create(pump, client);
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal("1 başarılı, 0 başarısız, 0 iptal, 0 bulunamadı", pump.Run(() => viewModel.SummaryText));

        // Act: yeni toplu iş başlıyor (iş bekletildiği için bitmedi).
        var gate = client.Hold("B.Two");
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));

        // Assert: eski özet çalışan işin yanında kalmaz.
        Assert.Null(pump.Run(() => viewModel.SummaryText));

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal("1 başarılı, 0 başarısız, 0 iptal, 0 bulunamadı", pump.Run(() => viewModel.SummaryText));
    }

    [Fact]
    public async Task Summary_text_follows_language_change()
    {
        // Arrange
        Loc.Instance.SetLanguage("tr");
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.Failed, "hata"));
        var (queue, viewModel) = Create(pump, client);
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        try
        {
            // Act
            Loc.Instance.SetLanguage("en");

            // Assert: aynı özet, yeni dilde ve bildirimle.
            Assert.Equal("1 succeeded, 1 failed, 0 cancelled, 0 not found", pump.Run(() => viewModel.SummaryText));
            Assert.Contains(nameof(OperationListViewModel.SummaryText), pump.Run(() => changed.ToList()));
        }
        finally
        {
            Loc.Instance.SetLanguage("tr");
        }
    }

    [Fact]
    public async Task Active_count_counts_queued_and_running()
    {
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var (queue, viewModel) = Create(pump, client);
        Assert.Equal(0, pump.Run(() => viewModel.ActiveCount));
        Assert.False(pump.Run(() => viewModel.HasActive));

        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("C.Three")));
        await gate.Started.WaitAsync(Timeout);
        await pump.FlushAsync();

        Assert.Equal(3, pump.Run(() => viewModel.ActiveCount));
        Assert.True(pump.Run(() => viewModel.HasActive));
        Assert.False(pump.Run(() => viewModel.HasFinished));

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(0, pump.Run(() => viewModel.ActiveCount));
        Assert.False(pump.Run(() => viewModel.HasActive));
        Assert.True(pump.Run(() => viewModel.HasFinished));
    }

    [Fact]
    public async Task Active_count_with_many_items()
    {
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("P0");
        var (queue, viewModel) = Create(pump, client);

        pump.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                queue.Enqueue(OperationKind.Install, Pkg($"P{i}"));
            }

            return 0;
        });

        await gate.Started.WaitAsync(Timeout);
        await pump.FlushAsync();
        Assert.Equal(200, pump.Run(() => viewModel.ActiveCount));

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(0, pump.Run(() => viewModel.ActiveCount));
        Assert.Equal(200, pump.Run(() => viewModel.Items.Count));
        Assert.True(pump.Run(() => viewModel.ClearFinishedCommand.CanExecute(null)));
        pump.Run(() =>
        {
            viewModel.ClearFinishedCommand.Execute(null);
            return 0;
        });
        Assert.Empty(pump.Run(() => viewModel.Items.ToList()));
        Assert.False(pump.Run(() => viewModel.HasFinished));
    }

    [Fact]
    public async Task Clearing_keeps_active_items_and_counts()
    {
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("B.Two");
        var (queue, viewModel) = Create(pump, client);
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));
        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("C.Three")));
        await gate.Started.WaitAsync(Timeout);
        await pump.FlushAsync();

        pump.Run(() =>
        {
            viewModel.ClearFinishedCommand.Execute(null);
            return 0;
        });

        Assert.Equal(2, pump.Run(() => viewModel.Items.Count));
        Assert.Equal(2, pump.Run(() => viewModel.ActiveCount));
        Assert.False(pump.Run(() => viewModel.HasFinished));
        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal(0, pump.Run(() => viewModel.ActiveCount));
        Assert.True(pump.Run(() => viewModel.HasFinished));
    }

    [Fact]
    public async Task Pane_opens_when_an_item_is_added()
    {
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var (queue, viewModel) = Create(pump, client);
        Assert.False(pump.Run(() => viewModel.IsPaneOpen));

        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        Assert.True(pump.Run(() => viewModel.IsPaneOpen));

        pump.Run(() =>
        {
            viewModel.ClosePaneCommand.Execute(null);
            return 0;
        });
        Assert.False(pump.Run(() => viewModel.IsPaneOpen));

        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("B.Two")));
        Assert.True(pump.Run(() => viewModel.IsPaneOpen));

        pump.Run(() =>
        {
            viewModel.TogglePaneCommand.Execute(null);
            return 0;
        });
        Assert.False(pump.Run(() => viewModel.IsPaneOpen));
        pump.Run(() =>
        {
            viewModel.TogglePaneCommand.Execute(null);
            return 0;
        });
        Assert.True(pump.Run(() => viewModel.IsPaneOpen));

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task Clear_finished_command_needs_a_finished_item()
    {
        using var pump = new PumpContext();
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var (queue, viewModel) = Create(pump, client);
        var canExecuteChanged = 0;
        viewModel.ClearFinishedCommand.CanExecuteChanged += (_, _) => Interlocked.Increment(ref canExecuteChanged);
        Assert.False(pump.Run(() => viewModel.ClearFinishedCommand.CanExecute(null)));

        pump.Run(() => queue.Enqueue(OperationKind.Install, Pkg("A.One")));
        await gate.Started.WaitAsync(Timeout);
        await pump.FlushAsync();
        Assert.False(pump.Run(() => viewModel.ClearFinishedCommand.CanExecute(null)));

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.True(pump.Run(() => viewModel.ClearFinishedCommand.CanExecute(null)));
        Assert.True(Volatile.Read(ref canExecuteChanged) > 0);
    }

    [Fact]
    public void Summary_stays_empty_after_language_change_when_no_batch_finished()
    {
        var viewModel = new OperationListViewModel(CreateQueue(new FakeWingetClient()));

        try
        {
            Loc.Instance.SetLanguage("en");

            Assert.Null(viewModel.SummaryText);
        }
        finally
        {
            Loc.Instance.SetLanguage("tr");
        }
    }
}
