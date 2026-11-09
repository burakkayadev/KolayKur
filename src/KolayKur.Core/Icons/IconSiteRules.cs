using System.Net;

namespace KolayKur.Core.Icons;

/// <summary>Hangi sitelerin simge için sorgulanacağını belirler.</summary>
public static class IconSiteRules
{
    // Birçok paketin "sitesi" yalnızca kod barındırma ya da mağaza sayfasıdır; onların simgesi uygulamanın simgesi değildir.
    private static readonly string[] GenericHosts =
    [
        "github.com", "githubusercontent.com", "gitlab.com", "bitbucket.org", "sourceforge.net", "codeberg.org",
        "apps.microsoft.com", "microsoft.com", "learn.microsoft.com", "windows.net", "azurewebsites.net",
    ];

    /// <summary>Simge aramak için kullanılabilir bir site mi (http/https, genel ve ortak olmayan bir ana makine).</summary>
    public static bool IsUsableWebsite(Uri? website) =>
        website is { IsAbsoluteUri: true } w
        && (w.Scheme == Uri.UriSchemeHttps || w.Scheme == Uri.UriSchemeHttp)
        && !IsLocalOrGeneric(w.Host);

    /// <summary>Sitenin ana makinesi için denenecek simge adresleri (yalnız https, aracı servis yok).</summary>
    public static IReadOnlyList<Uri> WebsiteIconCandidates(Uri website) =>
        !IsUsableWebsite(website)
            ? []
            : [new Uri($"https://{website.Host}/apple-touch-icon.png"), new Uri($"https://{website.Host}/favicon.ico")];

    /// <summary>Yerel ağa ya da bu bilgisayara işaret eden adresler indirilmez.</summary>
    public static bool IsPublicHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(host.Trim('[', ']'), out var ip) || !IsPrivate(ip);
    }

    private static bool IsLocalOrGeneric(string host) =>
        !IsPublicHost(host)
        || GenericHosts.Any(g => host.Equals(g, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + g, StringComparison.OrdinalIgnoreCase));

    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal)
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var b = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && (b[0] is 10 or 0 or 127 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127));
    }
}
