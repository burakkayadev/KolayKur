using System.Globalization;
using System.Text;

namespace KolayKur.Core.Support;

public sealed class FileLog(string directory)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly object gate = new();

    public string CurrentFile { get; } = Path.Combine(
        directory, $"kolaykur-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private void Write(string level, string message)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        var line = $"{timestamp} [{level}] {message}{Environment.NewLine}";

        lock (gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(CurrentFile, line, Utf8NoBom);
        }
    }
}
