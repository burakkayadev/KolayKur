using System.Diagnostics;
using System.Security.Cryptography;
using KolayKur.Core.Repair;

namespace KolayKur.Core.Tests.Repair;

public sealed class WindowsRepairActionsDownloadTests : IDisposable
{
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMilliseconds(400);

    private readonly string downloads =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"), "downloads");

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(downloads)!;
        if (Directory.Exists(parent))
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            respond(ct);
    }

    /// <summary>Parçaları aralıklarla verir; <c>stallAtEnd</c> ise son parçadan sonra iptale kadar hiç veri vermez.</summary>
    private sealed class ScriptedStream(byte[][] chunks, TimeSpan delayBetween, bool stallAtEnd) : Stream
    {
        private int next;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (next < chunks.Length)
            {
                await Task.Delay(delayBetween, ct);
                var chunk = chunks[next++];
                chunk.CopyTo(buffer);
                return chunk.Length;
            }

            if (stallAtEnd)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private WindowsRepairActions Create(Func<CancellationToken, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new StubHandler(respond)), StallTimeout, downloads);

    private static Task<HttpResponseMessage> Respond(Stream body) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StreamContent(body) });

    private static RepairFile FileOf(byte[] content) => new(
        "paket.bin",
        new Uri("https://github.com/microsoft/winget-cli/releases/download/v1/paket.bin"),
        content.Length,
        Convert.ToHexString(SHA256.HashData(content)));

    private sealed class NoProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }

    [Fact]
    public async Task Stalled_body_throws_download_stalled_and_deletes_the_part_file()
    {
        // Arrange: 10 bayt gelir, sonra bağlantı donar (Wi-Fi koptu).
        byte[] content = new byte[100];
        var actions = Create(_ => Respond(new ScriptedStream([new byte[10]], TimeSpan.Zero, stallAtEnd: true)));
        var stopwatch = Stopwatch.StartNew();

        // Act
        await Assert.ThrowsAsync<DownloadStalledException>(
            () => actions.DownloadAsync(FileOf(content), new NoProgress(), CancellationToken.None));

        // Assert
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(Path.Combine(downloads, "paket.bin.part")));
        Assert.False(File.Exists(Path.Combine(downloads, "paket.bin")));
    }

    [Fact]
    public async Task Stall_before_the_response_arrives_is_also_reported()
    {
        var actions = Create(async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage();
        });

        await Assert.ThrowsAsync<DownloadStalledException>(
            () => actions.DownloadAsync(FileOf(new byte[10]), new NoProgress(), CancellationToken.None));
    }

    [Fact]
    public async Task Slow_but_moving_download_does_not_trip_the_watchdog()
    {
        // Arrange: toplam süre (8 x 60 ms) bekleme süresini (400 ms) aşar ama hiçbir ara 400 ms'yi geçmez.
        var content = new byte[80];
        Random.Shared.NextBytes(content);
        var chunks = Enumerable.Range(0, 8).Select(i => content[(i * 10)..((i + 1) * 10)]).ToArray();
        var actions = Create(_ => Respond(new ScriptedStream(chunks, TimeSpan.FromMilliseconds(60), stallAtEnd: false)));

        // Act
        var path = await actions.DownloadAsync(FileOf(content), new NoProgress(), CancellationToken.None);

        // Assert
        Assert.Equal(Path.Combine(downloads, "paket.bin"), path);
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_stall()
    {
        using var cancel = new CancellationTokenSource();
        var actions = Create(_ => Respond(new ScriptedStream([new byte[10]], TimeSpan.Zero, stallAtEnd: true)));
        var running = actions.DownloadAsync(FileOf(new byte[100]), new NoProgress(), cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.False(File.Exists(Path.Combine(downloads, "paket.bin.part")));
    }

    [Fact]
    public void Cleanup_removes_downloaded_files_and_extracted_folders_but_tolerates_a_missing_folder()
    {
        var actions = Create(_ => throw new InvalidOperationException("ağ kullanılmamalı"));
        actions.CleanupDownloads();

        Directory.CreateDirectory(Path.Combine(downloads, "deps", "x64"));
        File.WriteAllText(Path.Combine(downloads, "deps", "x64", "a.appx"), "x");
        File.WriteAllText(Path.Combine(downloads, "deps.zip"), "x");
        File.WriteAllText(Path.Combine(downloads, "paket.msixbundle"), "x");

        actions.CleanupDownloads();

        Assert.True(!Directory.Exists(downloads) || !Directory.EnumerateFileSystemEntries(downloads).Any());
    }
}
