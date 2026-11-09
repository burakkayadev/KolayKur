using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.ViewModels;

// CountText dili Loc.Instance üzerinden okuduğu için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class UpdatesViewModelTests : QueueTestBase
{
    private readonly FakeWingetClient client = new()
    {
        Upgrades = [Pkg("A.One"), Pkg("B.Two"), Pkg("C.Three")],
    };

    [Fact]
    public async Task Load_shows_available_updates()
    {
        // Arrange
        var viewModel = new UpdatesViewModel(client, CreateQueue(client));

        // Act
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(["A.One", "B.Two", "C.Three"], viewModel.Updates.Select(p => p.Id));
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Upgrade_all_enqueues_every_item()
    {
        // Arrange
        var queue = CreateQueue(client);
        var viewModel = new UpdatesViewModel(client, queue);
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Act
        viewModel.UpgradeAllCommand.Execute(null);
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        Assert.Equal(
            [
                (OperationKind.Upgrade, "A.One"),
                (OperationKind.Upgrade, "B.Two"),
                (OperationKind.Upgrade, "C.Three"),
            ],
            queue.Items.Select(item => (item.Kind, item.Package.Id)));
    }

    [Fact]
    public async Task Upgrade_enqueues_one_upgrade()
    {
        // Arrange
        var queue = CreateQueue(client);
        var viewModel = new UpdatesViewModel(client, queue);

        // Act
        viewModel.UpgradeCommand.Execute(Pkg("B.Two"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        Assert.Equal([new FakeCall(OperationKind.Upgrade, "B.Two")], client.Calls);
    }

    [Fact]
    public async Task Updates_reload_after_batch()
    {
        // Arrange: three updates loaded; once the batch upgrades them, winget reports none left.
        var queue = CreateQueue(client);
        var viewModel = new UpdatesViewModel(client, queue);
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, client.UpgradeListCalls);
        client.Upgrades = [];

        // Act
        viewModel.UpgradeAllCommand.Execute(null);
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        await WaitUntilAsync(() => client.UpgradeListCalls == 2);
        await WaitUntilAsync(() => viewModel.Updates.Count == 0);
    }

    [Fact]
    public async Task Overlapping_loads_latest_wins()
    {
        // Arrange: the first load is still running when the second starts and finishes first.
        var manual = new ManualUpgradesClient();
        var viewModel = new UpdatesViewModel(manual, CreateQueue(client));
        var first = viewModel.LoadCommand.ExecuteAsync(null);
        var second = viewModel.LoadCommand.ExecuteAsync(null);

        // Act
        manual.Complete(1, [Pkg("New.One")]);
        await second.WaitAsync(Timeout);
        manual.Complete(0, [Pkg("Old.One"), Pkg("Old.Two")]);
        await first.WaitAsync(Timeout);

        // Assert
        Assert.Equal(["New.One"], viewModel.Updates.Select(p => p.Id));
    }

    [Fact]
    public async Task Offline_load_shows_retry_message_and_retry_recovers()
    {
        // Arrange
        var viewModel = new UpdatesViewModel(client, CreateQueue(client));
        client.ListException = new CatalogUnavailableException("çevrimdışı");

        // Act
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Error_NoInternet", viewModel.ErrorKey);
        Assert.Empty(viewModel.Updates);

        client.ListException = null;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorKey);
        Assert.Equal(3, viewModel.Updates.Count);
    }

    [Fact]
    public async Task Unexpected_error_shows_generic_message()
    {
        // Arrange
        var viewModel = new UpdatesViewModel(client, CreateQueue(client));
        client.ListException = new InvalidOperationException("beklenmeyen");

        // Act
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("Error_Generic", viewModel.ErrorKey);
    }

    [Fact]
    public async Task Update_count_follows_loaded_list()
    {
        var viewModel = new UpdatesViewModel(client, CreateQueue(client));
        Assert.Equal(0, viewModel.UpdateCount);
        Assert.False(viewModel.HasUpdates);
        Assert.Null(viewModel.CountText);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.UpdateCount);
        Assert.True(viewModel.HasUpdates);
        Assert.Equal(string.Format(Loc.Instance["Updates_Count"], 3), viewModel.CountText);
    }

    [Fact]
    public async Task Failed_load_hides_badge()
    {
        var viewModel = new UpdatesViewModel(client, CreateQueue(client));
        await viewModel.LoadCommand.ExecuteAsync(null);
        client.ListException = new CatalogUnavailableException("çevrimdışı");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(0, viewModel.UpdateCount);
        Assert.False(viewModel.HasUpdates);
        Assert.Null(viewModel.CountText);
    }

    [Fact]
    public async Task Count_text_follows_language()
    {
        var viewModel = new UpdatesViewModel(client, CreateQueue(client));
        await viewModel.LoadCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        try
        {
            Loc.Instance.SetLanguage("en");

            Assert.Equal("Updates available: 3", viewModel.CountText);
            Assert.Contains(nameof(UpdatesViewModel.CountText), changed);
        }
        finally
        {
            Loc.Instance.SetLanguage("tr");
        }
    }

    /// <summary>Her <c>GetUpgradesAsync</c> çağrısı, test tamamlayana kadar bekler.</summary>
    private sealed class ManualUpgradesClient : IWingetClient
    {
        private readonly List<TaskCompletionSource<IReadOnlyList<PackageInfo>>> pending = [];

        public void Complete(int callIndex, IReadOnlyList<PackageInfo> result) => pending[callIndex].SetResult(result);

        public Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct)
        {
            var source = new TaskCompletionSource<IReadOnlyList<PackageInfo>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(source);
            return source.Task;
        }

        public Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> InstallAsync(string id, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> UpgradeAsync(string id, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<KolayKur.Core.Icons.IconHints> GetIconHintsAsync(string packageId, string? source, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> UninstallAsync(string id, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Unexpected_error_is_written_to_the_log_file()
    {
        var viewModel = new UpdatesViewModel(client, CreateQueue(client), TestLog);
        client.ListException = new InvalidOperationException("guncelleme-patladi-xyz");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Error_Generic", viewModel.ErrorKey);
        Assert.Contains("guncelleme-patladi-xyz", File.ReadAllText(TestLog.CurrentFile), StringComparison.Ordinal);
    }
}
