using System.Net;
using System.Net.Sockets;

namespace KolayKur.Core.Icons;

/// <summary>Hangi sitelerin ve adreslerin simge için sorgulanacağını belirler.</summary>
public static class IconSiteRules
{
    // Birçok paketin "sitesi" yalnızca kod barındırma ya da mağaza sayfasıdır; onların simgesi uygulamanın simgesi değildir.
    private static readonly string[] GenericHosts =
    [
        "github.com", "githubusercontent.com", "gitlab.com", "bitbucket.org", "sourceforge.net", "codeberg.org",
        "apps.microsoft.com", "microsoft.com", "learn.microsoft.com", "windows.net", "azurewebsites.net",
    ];

    private static readonly string[] LocalSuffixes =
        [".localhost", ".local", ".internal", ".localdomain", ".home.arpa", ".lan", ".intranet", ".corp"];

    /// <summary>Simge aramak için kullanılabilir bir site mi (http/https, genel ve ortak olmayan bir ana makine).</summary>
    public static bool IsUsableWebsite(Uri? website) =>
        website is { IsAbsoluteUri: true } w
        && (w.Scheme == Uri.UriSchemeHttps || w.Scheme == Uri.UriSchemeHttp)
        && IsPublicHost(w.Host)
        && !IsGenericHost(w.Host);

    /// <summary>Sitenin ana makinesi için denenecek simge adresleri (yalnız https, aracı servis yok).</summary>
    public static IReadOnlyList<Uri> WebsiteIconCandidates(Uri website) =>
        !IsUsableWebsite(website)
            ? []
            : [new Uri($"https://{website.Host}/apple-touch-icon.png"), new Uri($"https://{website.Host}/favicon.ico")];

    /// <summary>
    /// Ana makine adı yerel ağa ya da bu bilgisayara işaret edebiliyorsa <c>false</c>: sondaki nokta atılır, tek etiketli adlar
    /// (<c>intranet</c>), <c>localhost</c> ve yerel son ekler reddedilir; IP adresi biçimindeyse <see cref="IsAllowedAddress"/> sorulur.
    /// Ad çözümlemesinin sonucu ayrıca bağlanırken denetlenir (<see cref="HttpIconFetcher"/>).
    /// </summary>
    public static bool IsPublicHost(string host)
    {
        var name = NormalizeHost(host);
        if (name.Length == 0)
        {
            return false;
        }

        if (IPAddress.TryParse(name, out var ip))
        {
            return IsAllowedAddress(ip);
        }

        return name.Contains('.')
            && !name.Equals("localhost", StringComparison.Ordinal)
            && !LocalSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal));
    }

    /// <summary>
    /// Bağlanılmasına izin verilen genel bir adres mi. Reddedilenler: geri döngü, özel (10/8, 172.16/12, 192.168/16), CGNAT 100.64/10,
    /// bağlantı-yerel, ULA fc00::/7, çok noktaya yayın, belirtilmemiş, yayın (255.255.255.255), 0.0.0.0/8, ayrılmış 240/4; ayrıca bunları
    /// gömen IPv4-eşlenmiş, IPv4-uyumlu, NAT64 (64:ff9b::/96) ve 6to4 (2002::/16) biçimleri.
    /// </summary>
    public static bool IsAllowedAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return IsAllowedV6(address);
        }

        return address.AddressFamily == AddressFamily.InterNetwork && IsAllowedV4(address.GetAddressBytes());
    }

    private static bool IsAllowedV4(byte[] b) =>
        !(b[0] == 0 || b[0] == 10 || b[0] == 127
          || (b[0] == 100 && b[1] is >= 64 and <= 127)
          || (b[0] == 169 && b[1] == 254)
          || (b[0] == 172 && b[1] is >= 16 and <= 31)
          || (b[0] == 192 && b[1] == 168)
          || b[0] >= 224);

    private static bool IsAllowedV6(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsAllowedV4(address.MapToIPv4().GetAddressBytes());
        }

        var b = address.GetAddressBytes();
        if (b[0] == 0xFF || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || IPAddress.IPv6Loopback.Equals(address))
        {
            return false;
        }

        if (b.Take(12).All(x => x == 0))
        {
            // "::" (belirtilmemiş) ve IPv4-uyumlu "::a.b.c.d".
            return IsAllowedV4(b[12..]) && b[12..].Any(x => x != 0);
        }

        // NAT64 64:ff9b::/96 ve 6to4 2002::/16 bir IPv4 adresi gömer.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.Skip(4).Take(8).All(x => x == 0))
        {
            return IsAllowedV4(b[12..]);
        }

        return !(b[0] == 0x20 && b[1] == 0x02) || IsAllowedV4(b[2..6]);
    }

    private static bool IsGenericHost(string host)
    {
        var name = NormalizeHost(host);
        return GenericHosts.Any(g => name.Equals(g, StringComparison.Ordinal) || name.EndsWith("." + g, StringComparison.Ordinal));
    }

    private static string NormalizeHost(string host) =>
        (host ?? string.Empty).Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();
}
