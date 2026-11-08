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
