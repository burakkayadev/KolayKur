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

    /// <summary>Content-Length bildirmeyen (akış) yanıt.</summary>
    public static HttpResponseMessage OkWithoutLength(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new NoLengthStream(body)) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requested.Enqueue(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request));
    }

    private sealed class NoLengthStream(byte[] body) : MemoryStream(body)
    {
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
