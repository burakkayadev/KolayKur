using KolayKur.Core.Lists;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.ViewModels;

public sealed class InstalledViewModelTests : QueueTestBase
{
    private const string Version = "1.29.380";

    private readonly FakeDialogService dialogs = new();

    private readonly FakeWingetClient client = new()
    {
        Installed = [Pkg("Git.Git"), Pkg("7zip.7zip")],
    };

    private (InstalledViewModel ViewModel, OperationQueue Queue) Create(IWingetClient? other = null)
    {
        var used = other ?? client;
        var queue = CreateQueue(used);
        return (new InstalledViewModel(used, queue, dialogs, () => Version), queue);
    }

    // ---- Yükleme ----

    [Fact]
    public async Task Load_shows_installed_packages()
    {
        var (viewModel, _) = Create();

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(["Git.Git", "7zip.7zip"], viewModel.Packages.Select(p => p.Id));
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Offline_load_shows_retry_message_and_retry_recovers()
    {
        var (viewModel, _) = Create();
        client.ListException = new CatalogUnavailableException("çevrimdışı");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Error_NoInternet", viewModel.ErrorKey);
        Assert.Empty(viewModel.Packages);

        client.ListException = null;
        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.ErrorKey);
        Assert.Equal(2, viewModel.Packages.Count);
    }

    [Fact]
    public async Task Unexpected_load_error_shows_generic_message()
    {
        var (viewModel, _) = Create();
        client.ListException = new InvalidOperationException("beklenmeyen");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Error_Generic", viewModel.ErrorKey);
    }

    [Fact]
    public async Task Overlapping_loads_latest_wins()
    {
        // Arrange: ilk yükleme sürerken ikincisi başlar ve önce biter.
        var manual = new ManualInstalledClient();
        var (viewModel, _) = Create(manual);
        var first = viewModel.LoadCommand.ExecuteAsync(null);
        var second = viewModel.LoadCommand.ExecuteAsync(null);

        // Act
        manual.Complete(1, [Pkg("New.One")]);
        await second.WaitAsync(Timeout);
        manual.Complete(0, [Pkg("Old.One"), Pkg("Old.Two")]);
        await first.WaitAsync(Timeout);

        // Assert
        Assert.Equal(["New.One"], viewModel.Packages.Select(p => p.Id));
    }

    [Fact]
    public async Task Installed_list_reloads_after_batch()
    {
        // Arrange: kaldırma bitince winget artık 7zip'i listelemez.
        dialogs.ConfirmResult = true;
        var (viewModel, queue) = Create();
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, client.InstalledListCalls);
        client.Installed = [Pkg("Git.Git")];

        // Act
        viewModel.UninstallCommand.Execute(Pkg("7zip.7zip"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        await WaitUntilAsync(() => client.InstalledListCalls == 2);
        await WaitUntilAsync(() => viewModel.Packages.Count == 1);
    }

    // ---- Kaldırma ----

    [Fact]
    public void Uninstall_asks_first()
    {
        // Arrange
        dialogs.ConfirmResult = false;
        var (viewModel, queue) = Create();

        // Act
        viewModel.UninstallCommand.Execute(Pkg("7zip.7zip"));

        // Assert
        Assert.Single(dialogs.Confirmed);
        Assert.Contains("7zip.7zip", dialogs.Confirmed[0], StringComparison.Ordinal);
        Assert.Empty(queue.Items);
        Assert.Empty(client.Calls);
    }

    [Fact]
    public async Task Uninstall_enqueues_after_yes()
    {
        // Arrange
        dialogs.ConfirmResult = true;
        var (viewModel, queue) = Create();

        // Act
        viewModel.UninstallCommand.Execute(Pkg("7zip.7zip"));
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        var item = Assert.Single(queue.Items);
        Assert.Equal(OperationKind.Uninstall, item.Kind);
        Assert.Equal("7zip.7zip", item.Package.Id);
        Assert.Equal([new FakeCall(OperationKind.Uninstall, "7zip.7zip")], client.Calls);
    }

    // ---- Dışa aktarma ----

    [Fact]
    public async Task Export_writes_the_loaded_packages_with_winget_version()
    {
        // Arrange
        var path = TempFile("liste.json");
        dialogs.SaveFilePath = path;
        var (viewModel, _) = Create();
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Act
        viewModel.ExportCommand.Execute(null);

        // Assert
        Assert.Equal(["Git.Git", "7zip.7zip"], PackageListFile.Load(path));
        Assert.Contains($"\"{Version}\"", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Export_cancelled_does_nothing()
    {
        // Arrange: dosya seçici iptal edildi (null).
        var path = TempFile("liste.json");
        dialogs.SaveFilePath = null;
        var (viewModel, _) = Create();
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Act
        viewModel.ExportCommand.Execute(null);

        // Assert
        Assert.Null(viewModel.ErrorKey);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Export_write_failure_sets_error_instead_of_crashing()
    {
        // Arrange: hedef yolun "klasörü" aslında bir dosya, yazma IOException ile bozulur.
        var blocker = TempFile("engel");
        await File.WriteAllTextAsync(blocker, "x");
        dialogs.SaveFilePath = Path.Combine(blocker, "liste.json");
        var (viewModel, _) = Create();
        await viewModel.LoadCommand.ExecuteAsync(null);

        // Act
        viewModel.ExportCommand.Execute(null);

        // Assert
        Assert.Equal("Error_ListSave", viewModel.ErrorKey);
    }

    // ---- İçe aktarma ----

    [Fact]
    public async Task Import_installs_every_id_from_the_file()
    {
        // Arrange
        var path = TempFile("liste.json");
        PackageListFile.Save(path, [Pkg("Git.Git"), Pkg("7zip.7zip")], Version);
        dialogs.OpenFilePath = path;
        var (viewModel, queue) = Create();

        // Act
        viewModel.ImportCommand.Execute(null);
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert: kimlik hem kimlik hem ad olarak girer (ad bilinmiyor).
        Assert.Equal(
            [(OperationKind.Install, "Git.Git"), (OperationKind.Install, "7zip.7zip")],
            queue.Items.Select(item => (item.Kind, item.Package.Id)));
        Assert.All(queue.Items, item => Assert.Equal(item.Package.Id, item.Package.Name));
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Import_of_broken_file_starts_nothing()
    {
        // Arrange
        var path = TempFile("bozuk.json");
        await File.WriteAllTextAsync(path, "{ bu json değil");
        dialogs.OpenFilePath = path;
        var (viewModel, queue) = Create();

        // Act
        viewModel.ImportCommand.Execute(null);

        // Assert
        Assert.Empty(queue.Items);
        Assert.Empty(client.Calls);
        Assert.Equal("Error_ListFile", viewModel.ErrorKey);
    }

    [Fact]
    public void Import_of_missing_file_starts_nothing()
    {
        dialogs.OpenFilePath = TempFile("yok.json");
        var (viewModel, queue) = Create();

        viewModel.ImportCommand.Execute(null);

        Assert.Empty(queue.Items);
        Assert.Equal("Error_ListFile", viewModel.ErrorKey);
    }

    [Fact]
    public void Import_cancelled_starts_nothing()
    {
        dialogs.OpenFilePath = null;
        var (viewModel, queue) = Create();

        viewModel.ImportCommand.Execute(null);

        Assert.Empty(queue.Items);
        Assert.Null(viewModel.ErrorKey);
    }

    [Fact]
    public async Task Import_unknown_package_continues()
    {
        // Arrange: ikinci paket katalogda yok.
        var path = TempFile("liste.json");
        PackageListFile.Save(path, [Pkg("Git.Git"), Pkg("Yok.Boyle")], Version);
        dialogs.OpenFilePath = path;
        client.SetOutcome("Yok.Boyle", new OperationOutcome(OperationStatus.NotFound, "bulunamadı"));
        var (viewModel, queue) = Create();

        // Act
        viewModel.ImportCommand.Execute(null);
        await queue.WhenIdleAsync().WaitAsync(Timeout);

        // Assert
        Assert.Equal(
            [OperationStatus.Succeeded, OperationStatus.NotFound],
            queue.Items.Select(item => item.Status));
    }

    /// <summary>Her <c>GetInstalledAsync</c> çağrısı, test tamamlayana kadar bekler.</summary>
    private sealed class ManualInstalledClient : IWingetClient
    {
        private readonly List<TaskCompletionSource<IReadOnlyList<PackageInfo>>> pending = [];

        public void Complete(int callIndex, IReadOnlyList<PackageInfo> result) => pending[callIndex].SetResult(result);

        public Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct)
        {
            var source = new TaskCompletionSource<IReadOnlyList<PackageInfo>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(source);
            return source.Task;
        }

        public Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> InstallAsync(string id, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> UpgradeAsync(string id, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> UninstallAsync(string id, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
