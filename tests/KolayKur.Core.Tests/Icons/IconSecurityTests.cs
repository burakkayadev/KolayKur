using System.Net;
using KolayKur.Core.Icons;

namespace KolayKur.Core.Tests.Icons;

public sealed class IconSiteRulesTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("::127.0.0.1")]
    [InlineData("::10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:7f00:1::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("fc00::1")]
    [InlineData("ff02::1")]
    public void Disallowed_addresses_are_rejected(string address) =>
        Assert.False(IconSiteRules.IsAllowedAddress(IPAddress.Parse(address)), address);

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.True(IconSiteRules.IsAllowedAddress(IPAddress.Parse(address)), address);

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost.")]
    [InlineData("LOCALHOST")]
    [InlineData("app.localhost")]
    [InlineData("intranet")]
    [InlineData("intranet.")]
    [InlineData("printer.local")]
    [InlineData("host.internal")]
    [InlineData("[::127.0.0.1]")]
    [InlineData("[64:ff9b::7f00:1]")]
    [InlineData("[::]")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("10.0.0.1")]
    [InlineData("[fd00::1]")]
    [InlineData("")]
    public void Local_hosts_are_not_public(string host) => Assert.False(IconSiteRules.IsPublicHost(host), host);

    [Theory]
    [InlineData("example.com")]
    [InlineData("example.com.")]
    [InlineData("www.videolan.org")]
    [InlineData("8.8.8.8")]
    [InlineData("[2606:4700:4700::1111]")]
    public void Public_hosts_are_allowed(string host) => Assert.True(IconSiteRules.IsPublicHost(host), host);

    [Theory]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("https://www.github.com/")]
    [InlineData("https://gitlab.com/x")]
    [InlineData("https://sourceforge.net/projects/x")]
    [InlineData("https://apps.microsoft.com/detail/x")]
    [InlineData("https://raw.githubusercontent.com/x")]
    [InlineData("https://localhost/x")]
    [InlineData("ftp://example.com/x")]
    public void Generic_or_unusable_sites_are_skipped(string url)
    {
        var site = new Uri(url);

        Assert.False(IconSiteRules.IsUsableWebsite(site));
        Assert.Empty(IconSiteRules.WebsiteIconCandidates(site));
    }

    [Fact]
    public void Real_vendor_site_gives_https_candidates_even_from_http_page()
    {
        var candidates = IconSiteRules.WebsiteIconCandidates(new Uri("http://www.netflix.com/"));

        Assert.Equal(
            [new Uri("https://www.netflix.com/apple-touch-icon.png"), new Uri("https://www.netflix.com/favicon.ico")],
            candidates);
    }

    [Fact]
    public async Task Connecting_to_a_loopback_name_is_blocked_before_any_connection()
    {
        await Assert.ThrowsAsync<HttpIconFetcher.BlockedAddressException>(async () =>
            await HttpIconFetcher.ConnectCheckedAsync(new DnsEndPoint("localhost", 443), CancellationToken.None));
        await Assert.ThrowsAsync<HttpIconFetcher.BlockedAddressException>(async () =>
            await HttpIconFetcher.ConnectCheckedAsync(new DnsEndPoint("127.0.0.1", 443), CancellationToken.None));
        await Assert.ThrowsAsync<HttpIconFetcher.BlockedAddressException>(async () =>
            await HttpIconFetcher.ConnectCheckedAsync(new DnsEndPoint("::ffff:10.0.0.1", 443), CancellationToken.None));
    }
}

public sealed class IconRankingTests
{
    private static IconCandidate C(string name, int px, IconThemeRank theme = IconThemeRank.Default) =>
        new(new Uri($"https://img.example.net/{name}.png"), px, theme);

    [Fact]
    public void Ranks_theme_first_then_size_closest_to_256_and_drops_high_contrast_and_http()
    {
        var ranked = IconRanking.Rank(
        [
            C("dark256", 256, IconThemeRank.Dark),
            C("light64", 64, IconThemeRank.Light),
            C("hc", 256, IconThemeRank.HighContrast),
            C("def32", 32),
            C("def96", 96),
            C("def256", 256),
            C("def64", 64),
            new IconCandidate(new Uri("http://img.example.net/plain.png"), 256, IconThemeRank.Default),
        ]);

        Assert.Equal(
            ["def256", "def96", "def64", "def32", "light64", "dark256"],
            ranked.Select(u => Path.GetFileNameWithoutExtension(u.AbsolutePath)));
    }
}

public sealed class IconCacheNameTests : IDisposable
{
    private readonly TempIconDir dir = new();

    public void Dispose() => dir.Dispose();

    [Theory]
    [InlineData(@"ARP\Machine\X64\{06b842bd-739c-4958-841e-b398791dfaf6}_is1")]
    [InlineData("..")]
    [InlineData(@"..\..\evil")]
    [InlineData("../../evil")]
    [InlineData("CON")]
    [InlineData("NUL.png")]
    [InlineData("a:b*c?d\"e<f>g|h")]
    public void Hostile_ids_stay_inside_the_cache_directory(string id)
    {
        var cache = dir.Cache();

        var path = cache.Put(id, "source", TestPng.Make());

        Assert.NotNull(path);
        Assert.Equal(Path.GetFullPath(dir.Path), Path.GetDirectoryName(Path.GetFullPath(path)));
        Assert.True(File.Exists(path));
        Assert.Equal(path, cache.TryGet(id));
    }

    [Fact]
    public void Very_long_ids_give_short_file_names()
    {
        var cache = dir.Cache();
        var id = new string('x', 500);

        var path = cache.Put(id, "source", TestPng.Make());

        Assert.NotNull(path);
        Assert.True(Path.GetFileName(path).Length < 140, Path.GetFileName(path));
        Assert.Equal(path, cache.TryGet(id));
    }

    [Fact]
    public void Ids_that_differ_only_in_invalid_characters_do_not_collide()
    {
        var cache = dir.Cache();

        var a = cache.Put("A\\B", "s", TestPng.Make(seed: 1));
        var b = cache.Put("A/B", "s", TestPng.Make(seed: 2));

        Assert.NotEqual(a, b);
        Assert.Equal(TestPng.Make(seed: 1), File.ReadAllBytes(cache.TryGet("A\\B")!));
    }

    [Fact]
    public void Invalidate_only_deletes_files_inside_the_cache()
    {
        var cache = dir.Cache();
        var inside = cache.Put("App", "s", TestPng.Make())!;
        var outside = Path.Combine(Path.GetTempPath(), "kolaykur-outside-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(outside, TestPng.Make());
        try
        {
            cache.Invalidate(outside);
            cache.Invalidate(inside);

            Assert.True(File.Exists(outside));
            Assert.False(File.Exists(inside));
        }
        finally
        {
            File.Delete(outside);
        }
    }
}

public sealed class WindowsIconDecoderTests
{
    private readonly WindowsIconDecoder decoder = new();

    [Fact]
    public async Task Gif_and_other_codecs_are_rejected_by_magic_bytes()
    {
        var gif = "GIF89a"u8.ToArray().Concat(new byte[64]).ToArray();
        var tiff = new byte[] { 0x49, 0x49, 0x2A, 0x00 }.Concat(new byte[64]).ToArray();
        var webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray().Concat(new byte[64]).ToArray();

        Assert.Null(await decoder.ToPngAsync(gif, CancellationToken.None));
        Assert.Null(await decoder.ToPngAsync(tiff, CancellationToken.None));
        Assert.Null(await decoder.ToPngAsync(webp, CancellationToken.None));
    }

    [Fact]
    public async Task Oversized_dimensions_are_rejected_before_pixels_are_read()
    {
        var huge = await RealPng.MakeAsync(WindowsIconDecoder.MaxSourcePixels + 76);

        Assert.Null(await decoder.ToPngAsync(huge, CancellationToken.None));
    }

    [Fact]
    public async Task Large_but_allowed_png_is_scaled_down_and_output_is_intact()
    {
        var png = await decoder.ToPngAsync(await RealPng.MakeAsync(300), CancellationToken.None);

        Assert.NotNull(png);
        Assert.Equal((IconLimits.MaxPixels, IconLimits.MaxPixels), PngIntegrity.Inspect(png));
    }

    [Fact]
    public void Decoder_is_chosen_from_the_leading_bytes()
    {
        Assert.NotNull(WindowsIconDecoder.DecoderIdFor([0x89, 0x50, 0x4E, 0x47, 0, 0]));
        Assert.NotNull(WindowsIconDecoder.DecoderIdFor([0, 0, 1, 0, 1, 0]));
        Assert.NotNull(WindowsIconDecoder.DecoderIdFor([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.NotNull(WindowsIconDecoder.DecoderIdFor([0x42, 0x4D, 0, 0]));
        Assert.Null(WindowsIconDecoder.DecoderIdFor([0x47, 0x49, 0x46, 0x38]));
        Assert.Null(WindowsIconDecoder.DecoderIdFor([]));
    }
}

public sealed class PngIntegrityTests
{
    [Fact]
    public void Intact_png_passes_and_any_flipped_body_byte_fails()
    {
        var png = TestPng.Make(size: 32, padding: 40);
        Assert.Equal((32, 32), PngIntegrity.Inspect(png));

        // IDAT gövdesi içindeki bir bayt: başlık hâlâ geçerli ama CRC tutmaz.
        var corrupted = (byte[])png.Clone();
        corrupted[8 + 25 + 8 + 5] ^= 0xFF;
        Assert.Null(PngIntegrity.Inspect(corrupted));
        Assert.Null(PngIntegrity.Inspect(png[..^3]));
        Assert.Null(PngIntegrity.Inspect(png.Concat(new byte[] { 0 }).ToArray()));
    }
}
