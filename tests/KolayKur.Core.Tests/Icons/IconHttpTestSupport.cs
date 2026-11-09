using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KolayKur.Core.Tests.Icons;

/// <summary>Ağa çıkmadan yanıt üreten sahte HTTP işleyicisi; istenen adresleri kaydeder.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> requested = new();

    public IReadOnlyList<string> Requested => [.. requested];

    public static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };

    public static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    /// <summary>Content-Length bildirmeyen (akış) yanıt: akış aranamaz olduğundan başlık hesaplanmaz.</summary>
    public static HttpResponseMessage OkWithoutLength(byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(body)) };
        return response.Content.Headers.ContentLength is null
            ? response
            : throw new InvalidOperationException("test yanıtı Content-Length içeriyor; akış sınırı sınanmaz.");
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requested.Enqueue(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request));
    }

    private sealed class NonSeekableStream(byte[] body) : Stream
    {
        private readonly MemoryStream inner = new(body);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => inner.ReadAsync(buffer, offset, count, ct);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => inner.ReadAsync(buffer, ct);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Gerçek Windows kodlayıcısıyla geçerli, tek renkli bir PNG üretir.</summary>
public static class RealPng
{
    public static async Task<byte[]> MakeAsync(int size)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, (uint)size, (uint)size, 96, 96, new byte[size * size * 4]);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }
}
