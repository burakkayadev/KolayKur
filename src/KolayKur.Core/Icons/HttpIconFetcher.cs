using System.Net;

namespace KolayKur.Core.Icons;

/// <summary>
/// Simgeyi doğrudan (aracı servis olmadan) indirir. Yalnız https; yönlendirmeler elle izlenir ve http'ye ya da yerel
/// adreslere giden yönlendirme reddedilir; en çok <see cref="IconLimits.MaxDownloadBytes"/>; toplam
/// <see cref="IconLimits.DownloadTimeout"/>. İndirilen veri yalnızca bellekte tutulur, hiçbir zaman çalıştırılmaz.
/// Reddedilen yanıt için <c>null</c>, ağ hatasında <see cref="HttpRequestException"/> ya da <see cref="IOException"/>.
/// </summary>
public sealed class HttpIconFetcher : IIconFetcher, IDisposable
{
    private const int MaxRedirects = 3;

    private readonly HttpClient client;

    public HttpIconFetcher()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true })
    {
    }

    public HttpIconFetcher(HttpMessageHandler handler)
    {
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KolayKur");
    }

    public async Task<byte[]?> FetchAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(IconLimits.DownloadTimeout);
        try
        {
            return await FetchCoreAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"icon download timed out: {url.Host}");
        }
    }

    public void Dispose() => client.Dispose();

    private async Task<byte[]?> FetchCoreAsync(Uri start, CancellationToken ct)
    {
        var url = start;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowed(url))
            {
                return null;
            }

            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    return null;
                }

                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }

            return response.IsSuccessStatusCode ? await ReadLimitedAsync(response, ct).ConfigureAwait(false) : null;
        }

        return null;
    }

    private static bool IsAllowed(Uri url) =>
        url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && IconSiteRules.IsPublicHost(url.Host);

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is > IconLimits.MaxDownloadBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > IconLimits.MaxDownloadBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : buffer.ToArray();
    }
}
