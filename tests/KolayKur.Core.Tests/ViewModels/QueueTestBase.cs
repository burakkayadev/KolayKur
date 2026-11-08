using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.ViewModels;

/// <summary>İşlem sırası kullanan ViewModel testleri için ortak kurulum: geçici günlük klasörü ve kuyruk.</summary>
public abstract class QueueTestBase : IDisposable
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    /// <summary>Geçici klasörde (test bitince silinir) bir dosya yolu verir; dosyanın kendisini oluşturmaz.</summary>
    protected string TempFile(string name)
    {
        var directory = Path.Combine(logDirectory, "files");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }

    protected OperationQueue CreateQueue(IWingetClient client) => new(client, TestLog);

    /// <summary>Test için geçici günlük (test bitince silinir); günlüğe yazılanı okumak için <see cref="FileLog.CurrentFile"/>.</summary>
    protected FileLog TestLog => new(logDirectory);

    protected static PackageInfo Pkg(string id) => new(id, id, "Yayıncı", "1.0", "2.0");

    protected static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Koşul zamanında gerçekleşmedi.");
            await Task.Delay(10);
        }
    }
}
