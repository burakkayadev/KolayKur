using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;

namespace KolayKur.Core.Tests.ViewModels;

// Özet metni Loc.Instance'tan geldiği için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class OperationListViewModelTests : QueueTestBase
{
    [Fact]
    public async Task Summary_text_counts_results()
    {
        // Arrange: one batch of three operations, the second one fails -> BatchSummary(2, 1, 0, 0).
        Loc.Instance.SetLanguage("tr");
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.Failed, "hata"));
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        Assert.Null(viewModel.SummaryText);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));

        // Act
        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        Assert.NotNull(viewModel.SummaryText);
        Assert.Contains("2", viewModel.SummaryText);
        Assert.Contains("1", viewModel.SummaryText);
        Assert.Equal("2 başarılı, 1 başarısız, 0 iptal, 0 bulunamadı", viewModel.SummaryText);
        Assert.Same(queue.Items, viewModel.Items);
    }

    [Fact]
    public async Task Summary_is_cleared_when_a_new_batch_starts()
    {
        // Arrange: ilk toplu iş bitti ve özeti görünüyor.
        Loc.Instance.SetLanguage("tr");
        var client = new FakeWingetClient();
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal("1 başarılı, 0 başarısız, 0 iptal, 0 bulunamadı", viewModel.SummaryText);

        // Act: yeni toplu iş başlıyor (iş bekletildiği için bitmedi).
        var gate = client.Hold("B.Two");
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));

        // Assert: eski özet çalışan işin yanında kalmaz.
        Assert.Null(viewModel.SummaryText);

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal("1 başarılı, 0 başarısız, 0 iptal, 0 bulunamadı", viewModel.SummaryText);
    }

    [Fact]
    public async Task Summary_text_follows_language_change()
    {
        // Arrange
        Loc.Instance.SetLanguage("tr");
        var client = new FakeWingetClient();
        client.SetOutcome("B.Two", new OperationOutcome(OperationStatus.Failed, "hata"));
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        try
        {
            // Act
            Loc.Instance.SetLanguage("en");

            // Assert: aynı özet, yeni dilde ve bildirimle.
            Assert.Equal("1 succeeded, 1 failed, 0 cancelled, 0 not found", viewModel.SummaryText);
            Assert.Contains(nameof(OperationListViewModel.SummaryText), changed);
        }
        finally
        {
            Loc.Instance.SetLanguage("tr");
        }
    }

    [Fact]
    public async Task Active_count_counts_queued_and_running()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        Assert.Equal(0, viewModel.ActiveCount);
        Assert.False(viewModel.HasActive);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));
        await gate.Started.WaitAsync(Timeout);

        Assert.Equal(3, viewModel.ActiveCount);
        Assert.True(viewModel.HasActive);
        Assert.False(viewModel.HasFinished);

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(0, viewModel.ActiveCount);
        Assert.False(viewModel.HasActive);
        Assert.True(viewModel.HasFinished);
    }

    [Fact]
    public async Task Active_count_with_many_items()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("P0");
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);

        for (var i = 0; i < 200; i++)
        {
            queue.Enqueue(OperationKind.Install, Pkg($"P{i}"));
        }

        await gate.Started.WaitAsync(Timeout);
        Assert.Equal(200, viewModel.ActiveCount);

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.Equal(0, viewModel.ActiveCount);
        Assert.Equal(200, viewModel.Items.Count);
        Assert.True(viewModel.ClearFinishedCommand.CanExecute(null));
        viewModel.ClearFinishedCommand.Execute(null);
        Assert.Empty(viewModel.Items);
        Assert.False(viewModel.HasFinished);
    }

    [Fact]
    public async Task Clearing_keeps_active_items_and_counts()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("B.Two");
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        queue.Enqueue(OperationKind.Install, Pkg("C.Three"));
        await gate.Started.WaitAsync(Timeout);

        viewModel.ClearFinishedCommand.Execute(null);

        Assert.Equal(2, viewModel.Items.Count);
        Assert.Equal(2, viewModel.ActiveCount);
        Assert.False(viewModel.HasFinished);
        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        Assert.Equal(0, viewModel.ActiveCount);
        Assert.True(viewModel.HasFinished);
    }

    [Fact]
    public async Task Pane_opens_when_an_item_is_added()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        Assert.False(viewModel.IsPaneOpen);

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        Assert.True(viewModel.IsPaneOpen);

        viewModel.ClosePaneCommand.Execute(null);
        Assert.False(viewModel.IsPaneOpen);

        queue.Enqueue(OperationKind.Install, Pkg("B.Two"));
        Assert.True(viewModel.IsPaneOpen);

        viewModel.TogglePaneCommand.Execute(null);
        Assert.False(viewModel.IsPaneOpen);
        viewModel.TogglePaneCommand.Execute(null);
        Assert.True(viewModel.IsPaneOpen);

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);
    }

    [Fact]
    public async Task Clear_finished_command_needs_a_finished_item()
    {
        var client = new FakeWingetClient();
        var gate = client.Hold("A.One");
        var queue = CreateQueue(client);
        var viewModel = new OperationListViewModel(queue);
        var canExecuteChanged = 0;
        viewModel.ClearFinishedCommand.CanExecuteChanged += (_, _) => canExecuteChanged++;
        Assert.False(viewModel.ClearFinishedCommand.CanExecute(null));

        queue.Enqueue(OperationKind.Install, Pkg("A.One"));
        await gate.Started.WaitAsync(Timeout);
        Assert.False(viewModel.ClearFinishedCommand.CanExecute(null));

        gate.Release();
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        Assert.True(viewModel.ClearFinishedCommand.CanExecute(null));
        Assert.True(canExecuteChanged > 0);
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
