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
}
