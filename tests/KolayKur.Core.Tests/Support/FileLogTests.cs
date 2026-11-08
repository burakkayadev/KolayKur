using KolayKur.Core.Support;

namespace KolayKur.Core.Tests.Support;

public sealed class FileLogTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Log_writes_under_path_with_spaces()
    {
        var directory = Path.Combine(root, "A B C", "Günlük");
        var log = new FileLog(directory);

        log.Info("Şişli'den merhaba");

        Assert.StartsWith(directory, log.CurrentFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Şişli'den merhaba", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Error_writes_message_and_exception()
    {
        var log = new FileLog(Path.Combine(root, "logs"));

        log.Error("Onarım başarısız", new InvalidOperationException("kötü gitti"));

        var text = File.ReadAllText(log.CurrentFile);
        Assert.Contains("Onarım başarısız", text, StringComparison.Ordinal);
        Assert.Contains("kötü gitti", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_are_appended()
    {
        var log = new FileLog(Path.Combine(root, "logs"));

        log.Info("birinci");
        log.Info("ikinci");

        var lines = File.ReadAllLines(log.CurrentFile);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void AppPaths_live_under_root()
    {
        Assert.StartsWith(AppPaths.Root, AppPaths.Logs, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.Root, AppPaths.Downloads, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("settings.json", Path.GetFileName(AppPaths.SettingsFile));
    }
}
