using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Winget;

public sealed class StoreFallbackTests : IDisposable
{
    private readonly string logDirectory = Path.Combine(Path.GetTempPath(), "kolaykur-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Store_failure_falls_back_to_winget()
    {
        var log = new FileLog(logDirectory);
        var calls = new List<bool>();

        var result = await StoreFallback.RunAsync(includeStore =>
        {
            calls.Add(includeStore);
            return includeStore
                ? throw new CatalogUnavailableException("store down")
                : Task.FromResult("winget-sonucu");
        }, log, CancellationToken.None);

        Assert.Equal("winget-sonucu", result);
        Assert.Equal([true, false], calls);
        // Kayıt özel durum ayrıntısıyla çok satırlıdır; tek kayıt = tek "[ERROR]" başlığı.
        var entries = File.ReadAllLines(log.CurrentFile).Where(line => line.Contains("[ERROR]", StringComparison.Ordinal));
        Assert.Single(entries);
    }

    [Fact]
    public async Task Store_success_does_not_retry_or_log()
    {
        var log = new FileLog(logDirectory);
        var calls = 0;

        var result = await StoreFallback.RunAsync(_ => Task.FromResult(++calls), log, CancellationToken.None);

        Assert.Equal(1, result);
        Assert.False(File.Exists(log.CurrentFile));
    }

    [Fact]
    public async Task Winget_failure_still_surfaces()
    {
        var log = new FileLog(logDirectory);

        await Assert.ThrowsAsync<CatalogUnavailableException>(() =>
            StoreFallback.RunAsync<int>(_ => throw new CatalogUnavailableException("down"), log, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var calls = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            StoreFallback.RunAsync<int>(_ => { calls++; throw new OperationCanceledException(); }, null, CancellationToken.None));

        Assert.Equal(1, calls);
    }
}
