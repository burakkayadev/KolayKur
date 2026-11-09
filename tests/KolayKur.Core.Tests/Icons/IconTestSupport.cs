using System.Collections.Concurrent;
using KolayKur.Core.Icons;

namespace KolayKur.Core.Tests.Icons;

/// <summary>Her test için geçici bir önbellek klasörü; test bitince silinir.</summary>
public sealed class TempIconDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kolaykur-icons-test-" + Guid.NewGuid().ToString("N"));

    public IconCache Cache(long maxBytes = IconLimits.MaxCacheBytes) => new(Path, maxBytes);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Temizlik en iyi çabadır.
        }
    }
}

/// <summary>Üstbilgi ve uç parçası geçerli, içeriği <paramref name="seed"/> ile ayrışan küçük sahte PNG üretir.</summary>
public static class TestPng
{
    public static byte[] Make(int size = 32, byte seed = 1, int padding = 0)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new List<byte>();
        ihdr.AddRange(BigEndian(size));
        ihdr.AddRange(BigEndian(size));
        ihdr.AddRange([8, 6, 0, 0, 0]);
        Chunk(ms, "IHDR", [.. ihdr]);
        Chunk(ms, "IDAT", [.. Enumerable.Repeat(seed, 8 + padding)]);
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(BigEndian(data.Length));
        s.Write(body);
        s.Write(BigEndian((int)PngIntegrity.Crc32(body)));
    }
}

/// <summary>PNG imzasıyla başlayan baytları kabul eder, diğerlerini (HTML vb.) reddeder.</summary>
public sealed class SignatureDecoder : IIconDecoder
{
    public Task<byte[]?> ToPngAsync(byte[] data, CancellationToken ct) =>
        Task.FromResult<byte[]?>(data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 ? data : null);
}

/// <summary>Adrese göre bayt (ya da <c>null</c>/hata) döner; istenen adresleri sırayla kaydeder.</summary>
public sealed class FakeFetcher : IIconFetcher
{
    private readonly ConcurrentQueue<Uri> requests = new();
    private int running;
    private int maxRunning;

    public ConcurrentDictionary<string, byte[]?> Responses { get; } = new();

    public Exception? Failure { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public IReadOnlyList<Uri> Requests => [.. requests];

    public int MaxConcurrent => Volatile.Read(ref maxRunning);

    public int Cancelled => Volatile.Read(ref cancelled);

    private int cancelled;

    public async Task<byte[]?> FetchAsync(Uri url, CancellationToken ct)
    {
        requests.Enqueue(url);
        var now = Interlocked.Increment(ref running);
        int seen;
        while (now > (seen = Volatile.Read(ref maxRunning)) && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen)
        {
        }

        try
        {
            if (Gate is not null)
            {
                try
                {
                    await Gate.Task.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelled);
                    throw;
                }
            }

            return Failure is not null ? throw Failure : Responses.GetValueOrDefault(url.AbsoluteUri);
        }
        finally
        {
            Interlocked.Decrement(ref running);
        }
    }
}

public sealed class FakeInstalledReader : IInstalledIconReader
{
    private int calls;

    public byte[]? Result { get; set; }

    public int Calls => Volatile.Read(ref calls);

    public Task<byte[]?> ReadAsync(string installedIconSource, CancellationToken ct)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult(Result);
    }
}
