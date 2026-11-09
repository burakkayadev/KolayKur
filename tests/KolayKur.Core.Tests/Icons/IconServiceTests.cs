using KolayKur.Core.Icons;
using KolayKur.Core.Models;
using KolayKur.Core.Tests.Fakes;

namespace KolayKur.Core.Tests.Icons;

public sealed class IconServiceTests : IDisposable
{
    private static readonly Uri CatalogIcon = new("https://store-images.example.net/a.png");
    private static readonly Uri Site = new("https://app.example.com/download");
    private const string TouchIcon = "https://app.example.com/apple-touch-icon.png";
    private const string Favicon = "https://app.example.com/favicon.ico";
    private static readonly PackageInfo Package = new("Vendor.App", "App", "Vendor", null, "1.0");

    private readonly TempIconDir dir = new();
    private readonly FakeWingetClient winget = new();
    private readonly FakeFetcher fetcher = new();
    private readonly FakeInstalledReader installed = new();

    public void Dispose() => dir.Dispose();

    private IconService Service(IconCache? cache = null) =>
        new(winget, fetcher, installed, new SignatureDecoder(), cache ?? dir.Cache());

    private static byte[] ReadAll(string? path) => File.ReadAllBytes(path ?? throw new InvalidOperationException("icon path expected"));

    [Fact]
    public async Task Uses_catalog_icon_first()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], Site, "pfn:Vendor.App_1");
        fetcher.Responses[CatalogIcon.AbsoluteUri] = TestPng.Make(seed: 1);
        fetcher.Responses[TouchIcon] = TestPng.Make(seed: 3);
        installed.Result = TestPng.Make(seed: 2);

        var path = await Service().GetIconPathAsync(Package, CancellationToken.None);

        Assert.Equal(TestPng.Make(seed: 1), ReadAll(path));
        Assert.Equal([CatalogIcon], fetcher.Requests);
        Assert.Equal(0, installed.Calls);
    }

    [Fact]
    public async Task Falls_back_to_installed_icon()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], Site, "pfn:Vendor.App_1");
        fetcher.Responses[CatalogIcon.AbsoluteUri] = null;
        fetcher.Responses[TouchIcon] = TestPng.Make(seed: 3);
        installed.Result = TestPng.Make(seed: 2);

        var path = await Service().GetIconPathAsync(Package, CancellationToken.None);

        Assert.Equal(TestPng.Make(seed: 2), ReadAll(path));
        Assert.Equal([CatalogIcon], fetcher.Requests);
        Assert.Equal(1, installed.Calls);
    }

    [Fact]
    public async Task Falls_back_to_website_icon()
    {
        winget.IconHintsById[Package.Id] = new IconHints([], Site, null);
        fetcher.Responses[TouchIcon] = null;
        fetcher.Responses[Favicon] = TestPng.Make(seed: 4);

        var path = await Service().GetIconPathAsync(Package, CancellationToken.None);

        Assert.Equal(TestPng.Make(seed: 4), ReadAll(path));
        Assert.Equal([new Uri(TouchIcon), new Uri(Favicon)], fetcher.Requests);
    }

    [Fact]
    public async Task Returns_null_when_nothing_found_and_caches_negative()
    {
        winget.IconHintsById[Package.Id] = new IconHints([], Site, null);
        var service = Service();

        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));
        var hintCalls = winget.IconHintCalls;
        var fetches = fetcher.Requests.Count;
        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));

        Assert.True(dir.Cache().IsNegative(Package.Id));
        Assert.Equal(hintCalls, winget.IconHintCalls);
        Assert.Equal(fetches, fetcher.Requests.Count);
    }

    [Fact]
    public async Task Rejects_http_and_http_redirects()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsoluteUri switch
        {
            "https://a.example.com/redirect-to-http.png" => StubHandler.Redirect("http://a.example.com/x.png"),
            "https://a.example.com/redirect-to-local.png" => StubHandler.Redirect("https://127.0.0.1/x.png"),
            "https://a.example.com/redirect-ok.png" => StubHandler.Redirect("https://b.example.com/i.png"),
            "https://b.example.com/i.png" => StubHandler.Ok([1, 2, 3]),
            _ => StubHandler.Ok([9]),
        });
        using var http = new HttpIconFetcher(handler);

        Assert.Null(await http.FetchAsync(new Uri("http://a.example.com/x.png"), CancellationToken.None));
        Assert.Null(await http.FetchAsync(new Uri("https://a.example.com/redirect-to-http.png"), CancellationToken.None));
        Assert.Null(await http.FetchAsync(new Uri("https://a.example.com/redirect-to-local.png"), CancellationToken.None));
        Assert.Null(await http.FetchAsync(new Uri("https://localhost/x.png"), CancellationToken.None));
        Assert.Equal([1, 2, 3], await http.FetchAsync(new Uri("https://a.example.com/redirect-ok.png"), CancellationToken.None));
        Assert.DoesNotContain(handler.Requested, u => u.StartsWith("http://", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requested, u => u.Contains("127.0.0.1", StringComparison.Ordinal) || u.Contains("localhost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_non_image_and_oversized()
    {
        // Hizmet katmanı: HTML dönen "favicon" çözülemez, harf kalır.
        winget.IconHintsById[Package.Id] = new IconHints([], Site, null);
        fetcher.Responses[TouchIcon] = "<html>404</html>"u8.ToArray();
        fetcher.Responses[Favicon] = "<html>nope</html>"u8.ToArray();
        Assert.Null(await Service().GetIconPathAsync(Package, CancellationToken.None));

        // İndirme katmanı: 5 MB (Content-Length ile) ve bildirimsiz 600 KB reddedilir.
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/declared.png" => StubHandler.Ok(new byte[5 * 1024 * 1024]),
            "/streamed.png" => StubHandler.OkWithoutLength(new byte[600 * 1024]),
            "/edge.png" => StubHandler.Ok(new byte[IconLimits.MaxDownloadBytes]),
            _ => StubHandler.Ok([1]),
        });
        using var http = new HttpIconFetcher(handler);
        Assert.Null(await http.FetchAsync(new Uri("https://a.example.com/declared.png"), CancellationToken.None));
        Assert.Null(await http.FetchAsync(new Uri("https://a.example.com/streamed.png"), CancellationToken.None));
        Assert.NotNull(await http.FetchAsync(new Uri("https://a.example.com/edge.png"), CancellationToken.None));

        // Gerçek Windows çözücü: HTML ve 8x8 resim reddedilir.
        var decoder = new WindowsIconDecoder();
        Assert.Null(await decoder.ToPngAsync("<html>404</html>"u8.ToArray(), CancellationToken.None));
        Assert.Null(await decoder.ToPngAsync(await RealPng.MakeAsync(8), CancellationToken.None));
        Assert.NotNull(await decoder.ToPngAsync(await RealPng.MakeAsync(32), CancellationToken.None));
    }

    [Fact]
    public async Task Network_failure_returns_null_without_throwing()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], Site, null);
        fetcher.Failure = new HttpRequestException("offline");
        var service = Service();

        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));
        Assert.False(dir.Cache().IsNegative(Package.Id));

        // Ağ geri gelince aynı paket yeniden denenir.
        fetcher.Failure = null;
        fetcher.Responses[CatalogIcon.AbsoluteUri] = TestPng.Make(seed: 5);
        Assert.NotNull(await service.GetIconPathAsync(Package, CancellationToken.None));

        // winget (COM) hatası da özel durum sızdırmaz ve "simge yok" diye saklanmaz.
        var other = new PackageInfo("Other.App", "Other", "Vendor", null, "1.0");
        winget.IconHintsException = new InvalidOperationException("com failed");
        Assert.Null(await service.GetIconPathAsync(other, CancellationToken.None));
        Assert.False(dir.Cache().IsNegative(other.Id));
    }

    [Fact]
    public async Task Same_icon_downloaded_once_under_concurrency()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], null, null);
        fetcher.Responses[CatalogIcon.AbsoluteUri] = TestPng.Make(seed: 6);
        fetcher.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service();

        var calls = Enumerable.Range(0, 20).Select(_ => service.GetIconPathAsync(Package, CancellationToken.None)).ToList();
        await WaitUntilAsync(() => fetcher.Requests.Count >= 1);
        fetcher.Gate.SetResult();
        var paths = await Task.WhenAll(calls);

        Assert.Single(fetcher.Requests);
        Assert.Equal(1, winget.IconHintCalls);
        Assert.All(paths, p => Assert.Equal(paths[0], p));
        Assert.NotNull(paths[0]);

        // Farklı paketlerde eşzamanlı indirme sınırı aşılmaz.
        fetcher.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var many = Enumerable.Range(0, 12).Select(i => new PackageInfo($"Many.App{i}", "App", "V", null, "1")).ToList();
        foreach (var p in many)
        {
            winget.IconHintsById[p.Id] = new IconHints([new Uri($"https://img.example.net/{p.Id}.png")], null, null);
            fetcher.Responses[$"https://img.example.net/{p.Id}.png"] = TestPng.Make(seed: 7);
        }

        var running = many.Select(p => service.GetIconPathAsync(p, CancellationToken.None)).ToList();
        await WaitUntilAsync(() => fetcher.MaxConcurrent >= IconLimits.MaxConcurrentDownloads);
        await Task.Delay(100);
        fetcher.Gate.SetResult();
        await Task.WhenAll(running);
        Assert.Equal(IconLimits.MaxConcurrentDownloads, fetcher.MaxConcurrent);
    }

    [Fact]
    public async Task Corrupt_cache_file_is_replaced()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], null, null);
        fetcher.Responses[CatalogIcon.AbsoluteUri] = TestPng.Make(seed: 8);
        var service = Service();
        var first = await service.GetIconPathAsync(Package, CancellationToken.None);
        Assert.NotNull(first);

        // Yarım kalmış dosya.
        await File.WriteAllBytesAsync(first, TestPng.Make(seed: 8)[..20]);
        var second = await service.GetIconPathAsync(Package, CancellationToken.None);

        Assert.Equal(2, fetcher.Requests.Count);
        Assert.Equal(TestPng.Make(seed: 8), ReadAll(second));

        // Çöp dosya, ve yeniden indirme de başarısızsa harf (null) kalır.
        await File.WriteAllBytesAsync(second!, "not a png at all, just text"u8.ToArray());
        fetcher.Responses[CatalogIcon.AbsoluteUri] = null;
        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));
    }

    [Fact]
    public async Task Corrupt_body_with_valid_header_is_refetched_and_ReportBroken_invalidates()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], null, null);
        fetcher.Responses[CatalogIcon.AbsoluteUri] = TestPng.Make(seed: 9, padding: 60);
        var service = Service();
        var first = await service.GetIconPathAsync(Package, CancellationToken.None);

        // Başlık ve uç parçası sağlam, IDAT gövdesinde bozulma: eski denetim bunu kabul ederdi.
        var bytes = File.ReadAllBytes(first!);
        bytes[8 + 25 + 8 + 10] ^= 0xFF;
        File.WriteAllBytes(first!, bytes);
        var second = await service.GetIconPathAsync(Package, CancellationToken.None);

        Assert.Equal(2, fetcher.Requests.Count);
        Assert.Equal(TestPng.Make(seed: 9, padding: 60), ReadAll(second));

        // Arayüz çözemediği dosyayı bildirirse silinir ve sonraki istek yeniden bulur.
        service.ReportBroken(second!);
        Assert.False(File.Exists(second));
        Assert.NotNull(await service.GetIconPathAsync(Package, CancellationToken.None));
        Assert.Equal(3, fetcher.Requests.Count);
    }

    [Fact]
    public async Task Incomplete_hints_are_not_negatively_cached()
    {
        winget.IconHintsById[Package.Id] = IconHints.Unknown;
        var service = Service();

        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));
        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));

        Assert.False(dir.Cache().IsNegative(Package.Id));
        Assert.Equal(2, winget.IconHintCalls);
    }

    [Fact]
    public async Task Negative_before_install_does_not_block_icon_after_install()
    {
        winget.IconHintsById[Package.Id] = IconHints.Empty;
        var service = Service();
        Assert.Null(await service.GetIconPathAsync(Package, CancellationToken.None));
        Assert.True(dir.Cache().IsNegative(Package.Id));

        // Kurulduktan sonra yerel simge okunabilir.
        var nowInstalled = Package with { InstalledVersion = "1.0" };
        winget.IconHintsById[Package.Id] = new IconHints([], null, "location:C:\\App");
        installed.Result = TestPng.Make(seed: 10);
        var path = await service.GetIconPathAsync(nowInstalled, CancellationToken.None);

        Assert.Equal(TestPng.Make(seed: 10), ReadAll(path));
        Assert.False(dir.Cache().IsNegative(Package.Id));
    }

    [Fact]
    public async Task Last_waiter_leaving_cancels_the_job_and_saves_no_negative()
    {
        winget.IconHintsById[Package.Id] = new IconHints([CatalogIcon], null, null);
        fetcher.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service();
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var a = service.GetIconPathAsync(Package, first.Token);
        var b = service.GetIconPathAsync(Package, second.Token);
        await WaitUntilAsync(() => fetcher.Requests.Count >= 1);

        first.Cancel();
        Assert.Null(await a);
        Assert.Equal(0, fetcher.Cancelled);
        second.Cancel();
        Assert.Null(await b);
        await WaitUntilAsync(() => fetcher.Cancelled >= 1);

        await Task.Delay(50);
        Assert.False(dir.Cache().IsNegative(Package.Id));
        Assert.Null(dir.Cache().TryGet(Package.Id));
    }

    [Fact]
    public void Cache_is_trimmed_above_limit()
    {
        var cache = dir.Cache(maxBytes: 3000);
        var paths = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var path = cache.Put($"App{i}", "src", TestPng.Make(seed: (byte)i, padding: 1000));
            Assert.NotNull(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10 + i));
            paths.Add(path);
        }

        cache.Trim();

        var remaining = Directory.EnumerateFiles(dir.Path).Sum(f => new FileInfo(f).Length);
        Assert.True(remaining <= 3000, $"{remaining} bayt kaldı");
        Assert.False(File.Exists(paths[0]));
        Assert.True(File.Exists(paths[^1]));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Zaman aşımı");
            await Task.Delay(10);
        }
    }
}
