using System.Net;
using System.Net.Sockets;

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

    // Vekil kullanılmaz: bağlantı geri çağrısı hedefin adresini kendisi çözüp denetler; vekil olsaydı denetlenen adres vekilin adresi olurdu.
    public HttpIconFetcher()
        : this(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = (context, ct) => ConnectCheckedAsync(context.DnsEndPoint, ct),
        })
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
        catch (HttpRequestException ex) when (ex.InnerException is BlockedAddressException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"icon download timed out: {url.Host}");
        }
    }

    public void Dispose() => client.Dispose();

    /// <summary>
    /// Adı kendisi çözer, yalnızca <see cref="IconSiteRules.IsAllowedAddress"/> geçen adreslere bağlanır ve bağlandığı <c>IPAddress</c>'i kullanır
    /// (ad çözümleme sonradan başka bir adres vermesin). Hiçbir adres uygun değilse <see cref="BlockedAddressException"/>.
    /// </summary>
    internal static async ValueTask<Stream> ConnectCheckedAsync(DnsEndPoint endPoint, CancellationToken ct)
    {
        var host = endPoint.Host;
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        var allowed = addresses.Where(IconSiteRules.IsAllowedAddress).ToList();
        if (allowed.Count == 0)
        {
            throw new BlockedAddressException(host);
        }

        Exception? last = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("icon host unreachable", last);
    }

    /// <summary>Ana makine yalnızca izin verilmeyen (özel/yerel) adreslere çözülüyor.</summary>
    internal sealed class BlockedAddressException(string host) : IOException($"blocked address for {host}");

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
