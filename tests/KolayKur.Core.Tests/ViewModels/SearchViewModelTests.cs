using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.ViewModels;

// Sonuç sayısı metni Loc.Instance'tan geldiği için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class SearchViewModelTests : QueueTestBase
{
    private readonly FakeWingetClient client = new()
    {
        Catalog = [Pkg("Alpha.App"), Pkg("Beta.App"), Pkg("Gamma.Tool")],
    };

    private SearchViewModel CreateViewModel(out OperationQueue queue)
    {
        queue = CreateQueue(client);
        return new SearchViewModel(client, queue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_query_does_not_call_winget(string query)
    {
        // Arrange
        var viewModel = CreateViewModel(out _);
        viewModel.Query = query;

        // Act
        await viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(client.SearchCalls);
        Assert.Empty(viewModel.Results);
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Newer_search_wins()
    {
        // Arrange: the older search ("alpha") finishes only after the newer one ("beta") and ignores cancellation.
        var viewModel = CreateViewModel(out _);
        var olderGate = client.HoldSearch("alpha");
        viewModel.Query = "alpha";
        var older = viewModel.SearchCommand.ExecuteAsync(null);
        await olderGate.Started.WaitAsync(Timeout);

        // Act
        viewModel.Query = "beta";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Equal(["Beta.App"], viewModel.Results.Select(r => r.Package.Id));
        olderGate.Release();
        await older.WaitAsync(Timeout);

        // Assert: the late, stale result never overwrites the newer one, and the older search was told to cancel.
        Assert.Equal(["Beta.App"], viewModel.Results.Select(r => r.Package.Id));
        Assert.Null(viewModel.ErrorKey);
        Assert.True(client.SearchCalls[0].Token.IsCancellationRequested);
        Assert.False(client.SearchCalls[1].Token.IsCancellationRequested);
    }

    [Fact]
    public async Task Stale_failure_does_not_replace_newer_results()
    {
        // Arrange: the older search will fail late, after the newer one succeeded.
        var viewModel = CreateViewModel(out _);
        var olderGate = client.HoldSearch("alpha");
        viewModel.Query = "alpha";
        var older = viewModel.SearchCommand.ExecuteAsync(null);
        await olderGate.Started.WaitAsync(Timeout);
        viewModel.Query = "beta";
        await viewModel.SearchCommand.ExecuteAsync(null);

        // Act
        client.ListException = new CatalogUnavailableException("çevrimdışı");
        olderGate.Release();
        await older.WaitAsync(Timeout);

        // Assert
        Assert.Null(viewModel.ErrorKey);
        Assert.Equal(["Beta.App"], viewModel.Results.Select(r => r.Package.Id));
    }

    [Fact]
    public async Task Install_selected_enqueues_each()
    {
        // Arrange: 3 results, 2 of them checked.
        var viewModel = CreateViewModel(out var queue);
        viewModel.Query = ".";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Equal(3, viewModel.Results.Count);
        viewModel.Results[0].IsSelected = true;
        viewModel.Results[2].IsSelected = true;

        // Act
        viewModel.InstallSelectedCommand.Execute(null);
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        Assert.Equal(
            [(OperationKind.Install, "Alpha.App"), (OperationKind.Install, "Gamma.Tool")],
            queue.Items.Select(item => (item.Kind, item.Package.Id)));
    }

    [Fact]
    public async Task Selected_count_follows_checkboxes()
    {
        // Arrange: 3 results.
        var viewModel = CreateViewModel(out _);
        viewModel.Query = ".";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.False(viewModel.InstallSelectedCommand.CanExecute(null));

        // Act + Assert: two selected.
        viewModel.Results[0].IsSelected = true;
        viewModel.Results[2].IsSelected = true;
        Assert.Equal(2, viewModel.SelectedCount);
        Assert.True(viewModel.InstallSelectedCommand.CanExecute(null));

        // Act + Assert: selection cleared.
        viewModel.Results[0].IsSelected = false;
        viewModel.Results[2].IsSelected = false;
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.False(viewModel.InstallSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Selected_count_resets_when_results_are_replaced()
    {
        var viewModel = CreateViewModel(out _);
        viewModel.Query = ".";
        await viewModel.SearchCommand.ExecuteAsync(null);
        viewModel.Results[0].IsSelected = true;
        var old = viewModel.Results[0];
        Assert.Equal(1, viewModel.SelectedCount);

        viewModel.Query = "beta";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.Equal(0, viewModel.SelectedCount);
        old.IsSelected = false; // eski satır artık sayıyı etkilemez
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.False(viewModel.InstallSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Result_count_text_shows_query_and_count()
    {
        Loc.Instance.SetLanguage("tr");
        client.Catalog = [Pkg("Git.Git"), Pkg("GitHub.GitHubDesktop"), Pkg("Other.App")];
        var viewModel = CreateViewModel(out _);
        Assert.Null(viewModel.ResultCountText);

        viewModel.Query = "git";
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(Loc.Instance["Search_ResultCount"], "git", 2), viewModel.ResultCountText);
    }

    [Fact]
    public async Task Result_count_text_follows_language()
    {
        Loc.Instance.SetLanguage("tr");
        try
        {
            client.Catalog = [Pkg("Git.Git"), Pkg("GitHub.GitHubDesktop")];
            var viewModel = CreateViewModel(out _);
            viewModel.Query = "git";
            await viewModel.SearchCommand.ExecuteAsync(null);
            var changed = new List<string?>();
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SearchViewModel.ResultCountText))
                {
                    changed.Add(e.PropertyName);
                }
            };

            Loc.Instance.SetLanguage("en");

            Assert.NotEmpty(changed);
            Assert.Equal(string.Format(Loc.Instance["Search_ResultCount"], "git", 2), viewModel.ResultCountText);
            Assert.Equal("Results for “git”: 2", viewModel.ResultCountText);
        }
        finally
        {
            Loc.Instance.SetLanguage("tr");
        }
    }

    [Fact]
    public async Task Result_count_text_is_cleared_on_failure()
    {
        Loc.Instance.SetLanguage("tr");
        var viewModel = CreateViewModel(out _);
        viewModel.Query = "alpha";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.ResultCountText);

        client.ListException = new CatalogUnavailableException("çevrimdışı");
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ResultCountText);
    }

    [Fact]
    public async Task Install_enqueues_one_install()
    {
        // Arrange
        var viewModel = CreateViewModel(out var queue);
        var package = Pkg("Beta.App");

        // Act
        viewModel.InstallCommand.Execute(package);
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        Assert.Equal([new FakeCall(OperationKind.Install, "Beta.App")], client.Calls);
    }

    [Fact]
    public async Task Offline_search_shows_retry_message()
    {
        // Arrange
        var viewModel = CreateViewModel(out _);
        client.ListException = new CatalogUnavailableException("çevrimdışı");
        viewModel.Query = "alpha";

        // Act
        await viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Error_NoInternet", viewModel.ErrorKey);
        Assert.Empty(viewModel.Results);
    }

    [Fact]
    public async Task Retry_reruns_last_search_and_clears_error()
    {
        // Arrange: offline first, then the connection comes back.
        var viewModel = CreateViewModel(out _);
        client.ListException = new CatalogUnavailableException("çevrimdışı");
        viewModel.Query = "alpha";
        await viewModel.SearchCommand.ExecuteAsync(null);
        client.ListException = null;
        viewModel.Query = "text the user typed but did not search";

        // Act
        await viewModel.RetryCommand.ExecuteAsync(null);

        // Assert: the retry used the last searched text, not the box content.
        Assert.Null(viewModel.ErrorKey);
        Assert.Equal(["Alpha.App"], viewModel.Results.Select(r => r.Package.Id));
        Assert.Equal(["alpha", "alpha"], client.SearchCalls.Select(call => call.Query));
    }

    [Fact]
    public async Task Unexpected_error_shows_generic_message()
    {
        // Arrange
        var viewModel = CreateViewModel(out _);
        client.ListException = new InvalidOperationException("beklenmeyen");
        viewModel.Query = "alpha";

        // Act
        await viewModel.SearchCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Error_Generic", viewModel.ErrorKey);
    }

    [Fact]
    public async Task Unexpected_error_is_written_to_the_log_file()
    {
        var viewModel = new SearchViewModel(client, CreateQueue(client), TestLog);
        client.ListException = new InvalidOperationException("arama-patladi-xyz");
        viewModel.Query = "alpha";

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Error_Generic", viewModel.ErrorKey);
        Assert.Contains("arama-patladi-xyz", File.ReadAllText(TestLog.CurrentFile), StringComparison.Ordinal);
    }
}
