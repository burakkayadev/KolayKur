using System.IO.Compression;
using System.Text.Json;
using KolayKur.Core.Support;
using Windows.Management.Deployment;

namespace KolayKur.Core.Repair;

public sealed class WindowsRepairActions : IRepairActions
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/microsoft/winget-cli/releases/latest";
    private const string DownloadPathPrefix = "/microsoft/winget-cli/releases/download/";
    private const string AppInstallerAssetName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle";
    private const string DependenciesAssetName = "DesktopAppInstaller_Dependencies.zip";
    private const string AppInstallerFamilyName = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
    private const string DigestPrefix = "sha256:";
    private const string X64Folder = "x64";
    private const int Sha256HexLength = 64;
    private const int CopyBufferSize = 81920;

    /// <summary>Bu süre boyunca hiç bayt gelmezse indirme takılmış sayılır ve iptal edilir.</summary>
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpClient SharedHttp = CreateHttpClient();

    private readonly HttpClient http;
    private readonly TimeSpan stallTimeout;
    private readonly string downloadsDirectory;

    public WindowsRepairActions()
        : this(SharedHttp, DefaultStallTimeout, AppPaths.Downloads)
    {
    }

    /// <summary>Testler için: HTTP istemcisi, takılma süresi ve indirme klasörü dışarıdan verilir.</summary>
    internal WindowsRepairActions(HttpClient http, TimeSpan stallTimeout, string downloadsDirectory)
    {
        this.http = http;
        this.stallTimeout = stallTimeout;
        this.downloadsDirectory = downloadsDirectory;
    }

    public async Task<WingetRelease> GetLatestReleaseAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<string> DownloadAsync(RepairFile file, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(downloadsDirectory);
        var finalPath = Path.Combine(downloadsDirectory, Path.GetFileName(file.Name));
        var partPath = finalPath + ".part";

        try
        {
            await CopyToFileAsync(file, partPath, progress, ct);
            DownloadVerifier.Verify(partPath, file.Size, file.Sha256);
            File.Move(partPath, finalPath, overwrite: true);
        }
        catch
        {
            File.Delete(partPath);
            throw;
        }

        return finalPath;
    }

    public IReadOnlyList<string> ExtractDependencies(string zipPath)
    {
        var target = Path.Combine(
            Path.GetDirectoryName(zipPath)!, Path.GetFileNameWithoutExtension(zipPath));
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        return ExtractX64(zipPath, target);
    }

    public async Task AddPackageAsync(string packagePath, IReadOnlyList<string> dependencyPaths, CancellationToken ct)
    {
        var dependencies = dependencyPaths.Select(path => new Uri(path)).ToList();
        var result = await new PackageManager()
            .AddPackageAsync(new Uri(packagePath), dependencies, DeploymentOptions.None)
            .AsTask(ct);
        EnsureDeployed(result, $"{Path.GetFileName(packagePath)} kurulamadı");
    }

    public async Task RegisterAppInstallerAsync(CancellationToken ct)
    {
        var result = await new PackageManager()
            .RegisterPackageByFamilyNameAsync(AppInstallerFamilyName, [], DeploymentOptions.None, null, [])
            .AsTask(ct);
        EnsureDeployed(result, "Uygulama Yükleyici kaydedilemedi");
    }

    internal static WingetRelease ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = ReadString(root, "tag_name")
            ?? throw new DownloadVerificationException("Sürüm bilgisinde tag_name yok.");
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new DownloadVerificationException("Sürüm bilgisinde assets listesi yok.");
        }

        return new WingetRelease(
            tag,
            ReadAsset(assets, AppInstallerAssetName),
            ReadAsset(assets, DependenciesAssetName));
    }

    internal static IReadOnlyList<string> ExtractX64(string zipPath, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var allowedRoot = Path.GetFullPath(Path.Combine(targetDirectory, X64Folder)) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        var plan = new List<(ZipArchiveEntry Entry, string Destination)>();
        foreach (var entry in archive.Entries)
        {
            var isX64File = entry.Name.Length > 0
                && entry.FullName.StartsWith(X64Folder + "/", StringComparison.OrdinalIgnoreCase);
            if (!isX64File)
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            if (!destination.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Zip girdisi hedef klasörün dışına çıkıyor: {entry.FullName}");
            }

            plan.Add((entry, destination));
        }

        foreach (var (entry, destination) in plan)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        return plan.Select(item => item.Destination).ToList();
    }

    private static RepairFile ReadAsset(JsonElement assets, string assetName)
    {
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(ReadString(asset, "name"), assetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return new RepairFile(
                assetName,
                ReadDownloadUrl(asset, assetName),
                ReadSize(asset, assetName),
                ReadSha256(asset, assetName));
        }

        throw new DownloadVerificationException($"Son sürümde {assetName} dosyası yok.");
    }

    private static Uri ReadDownloadUrl(JsonElement asset, string assetName)
    {
        var text = ReadString(asset, "browser_download_url");
        var isOfficial = Uri.TryCreate(text, UriKind.Absolute, out var url)
            && url.Scheme == Uri.UriSchemeHttps
            && string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && url.AbsolutePath.StartsWith(DownloadPathPrefix, StringComparison.OrdinalIgnoreCase);
        return isOfficial
            ? url!
            : throw new DownloadVerificationException(
                $"{assetName} adresi microsoft/winget-cli sürümlerinden gelmiyor: {text}");
    }

    private static long ReadSize(JsonElement asset, string assetName)
    {
        var hasSize = asset.TryGetProperty("size", out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out var size)
            && size > 0;
        return hasSize
            ? element.GetInt64()
            : throw new DownloadVerificationException($"{assetName} için boyut bilgisi yok, dosya kurulmaz.");
    }

    private static string ReadSha256(JsonElement asset, string assetName)
    {
        var digest = ReadString(asset, "digest");
        if (digest is null || !digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadVerificationException($"{assetName} için SHA-256 özeti yok, dosya kurulmaz.");
        }

        var hash = digest[DigestPrefix.Length..];
        return hash.Length == Sha256HexLength && hash.All(Uri.IsHexDigit)
            ? hash
            : throw new DownloadVerificationException($"{assetName} için SHA-256 özeti geçersiz.");
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Dosyayı indirir. <c>ResponseHeadersRead</c> ile <see cref="HttpClient.Timeout"/> gövdeyi kapsamadığı için ayrı bir
    /// bekçi vardır: <see cref="stallTimeout"/> boyunca hiç bayt gelmezse (her parçada süre sıfırlanır) indirme iptal edilir
    /// ve <see cref="DownloadStalledException"/> atılır. Çağıranın kendi iptali olduğu gibi yansır.
    /// </summary>
    private async Task CopyToFileAsync(RepairFile file, string path, IProgress<double> progress, CancellationToken ct)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(stallTimeout);
        try
        {
            await CopyCoreAsync(file, path, progress, stall, ct);
        }
        catch (OperationCanceledException ex) when (stall.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new DownloadStalledException(file.Name, ex);
        }
    }

    private async Task CopyCoreAsync(
        RepairFile file, string path, IProgress<double> progress, CancellationTokenSource stall, CancellationToken ct)
    {
        using var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
        await using var target = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

        var buffer = new byte[CopyBufferSize];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
        {
            stall.CancelAfter(stallTimeout);
            await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
            copied += read;
            progress.Report(Math.Min(1.0, (double)copied / file.Size));
        }
    }

    /// <summary>İndirme klasörünü (indirilen dosyalar ve çıkarılan bağımlılık klasörleri) siler; klasör yoksa bir şey yapmaz.</summary>
    public void CleanupDownloads()
    {
        if (Directory.Exists(downloadsDirectory))
        {
            Directory.Delete(downloadsDirectory, recursive: true);
        }
    }

    private static void EnsureDeployed(DeploymentResult result, string message)
    {
        if (result.ExtendedErrorCode is { } error)
        {
            throw new InvalidOperationException($"{message}: {result.ErrorText}", error);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KolayKur");
        return client;
    }
}
