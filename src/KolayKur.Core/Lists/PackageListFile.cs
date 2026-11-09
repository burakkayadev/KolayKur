using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KolayKur.Core.Models;

namespace KolayKur.Core.Lists;

/// <summary>Paket listesi dosyası okunamıyor: JSON bozuk veya <c>winget export</c> şemasına uymuyor.</summary>
public class PackageListFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Liste dosyası çok büyük (boyut veya paket sayısı sınırı aşıldı); okunmaz, hiçbir şey kurulmaz.</summary>
public sealed class PackageListTooLargeException(string message) : PackageListFormatException(message);

/// <summary>
/// Paket listesi dosyası. Biçim, <c>winget export</c> çıktısının şemasıdır (<c>winget import -i dosya</c> bunu okur):
/// <c>Sources[].Packages[].PackageIdentifier</c>. Dosyayı <c>winget export</c> da yazmış olabilir; yalnızca kimlikler okunur.
/// </summary>
public static class PackageListFile
{
    /// <summary>Okunacak liste dosyasının en büyük boyutu (bayt); gerçek listeler birkaç yüz KB bile tutmaz.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>Bir listeden kurulabilecek en çok farklı paket sayısı.</summary>
    public const int MaxPackages = 500;

    private const string SchemaUrl = "https://aka.ms/winget-packages.schema.2.0.json";

    // `SourceDetails` değerleri winget'in kendi dışa aktarımındaki gibidir; `winget import` kaynağı bunlarla eşler.
    private static readonly SourceDetailsData WingetSource = new(
        "https://cdn.winget.microsoft.com/cache", "Microsoft.Winget.Source_8wekyb3d8bbwe", PackageSources.Winget, "Microsoft.PreIndexed.Package");

    private static readonly SourceDetailsData StoreSource = new(
        "https://storeedgefd.dsx.mp.microsoft.com/v9.0", "StoreEdgeFD", PackageSources.MicrosoftStore, "Microsoft.Rest");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Yerel katalog (GetInstalledAsync) winget kaynağı olmayan kayıtları da listeler. Bunların kimliği Add/Remove Programs
    // ("ARP\Machine\X64\...") veya kaynaksız MSIX ("MSIX\Ad_Sürüm_mimari__yayıncı") biçimindedir; `winget export` bunları yazmaz.
    private static readonly string[] SourcelessIdPrefixes = [@"ARP\", @"MSIX\"];

    /// <summary>
    /// Paket <c>winget export</c> dosyasına yazılabilir mi? Kaynağı winget veya msstore olmalı; kaynaksız kayıtlar,
    /// ARP\ ve MSIX\ ile başlayan kimlikler ve boş kimlikler yazılmaz. <see cref="Save"/> bilinen kaynaklı paketleri
    /// yazar, diğerlerini atlar.
    /// </summary>
    public static bool IsExportable(PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return IsKnownSource(package.Source)
            && !string.IsNullOrWhiteSpace(package.Id)
            && !Array.Exists(SourcelessIdPrefixes, prefix => package.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Paketleri kaynak başına bir <c>Sources</c> girdisi olarak yazar (önce winget, sonra msstore; yalnızca paketi
    /// olan kaynaklar, hiç paket yoksa paketsiz bir winget girdisi). Dışa aktarılamayan paketler atlanır. Önce geçici dosyaya
    /// yazıp asıl dosyanın yerine koyar, yarım dosya kalmaz. G/Ç hataları (<see cref="IOException"/> vb.) çağırana yansır.
    /// </summary>
    public static void Save(string path, IEnumerable<PackageInfo> packages, string wingetVersion)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var exportable = packages.Where(IsExportable).ToList();
        var sources = new List<ExportSource>();
        foreach (var details in new[] { WingetSource, StoreSource })
        {
            if (exportable.Any(package => SourceIs(package, details.Name)))
            {
                sources.Add(SourceOf(details, exportable));
            }
        }

        if (sources.Count == 0)
        {
            // Boş liste de geçerli bir `winget export` dosyası olsun: paketsiz tek winget girdisi.
            sources.Add(SourceOf(WingetSource, exportable));
        }

        var export = new ExportFile
        {
            CreationDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'-00:00'", CultureInfo.InvariantCulture),
            Sources = sources,
            WinGetVersion = wingetVersion,
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("Liste dosyasının klasörü belirlenemedi.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(export, JsonOptions), Utf8NoBom);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Dosyadaki tüm kaynakların paket kimliklerini sırasıyla döndürür; kimlikler kırpılır, büyük/küçük harf ayırmadan
    /// yinelenenler atılır. Geçersiz JSON veya şemaya uymayan içerik <see cref="PackageListFormatException"/> atar;
    /// <see cref="MaxFileBytes"/>'tan büyük dosya (okunmadan) ya da <see cref="MaxPackages"/>'tan çok farklı kimlik
    /// <see cref="PackageListTooLargeException"/> atar. Dosya okunamıyorsa <see cref="IOException"/> /
    /// <see cref="UnauthorizedAccessException"/> çağırana yansır.
    /// </summary>
    public static IReadOnlyList<string> Load(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new PackageListTooLargeException($"Liste dosyası {MaxFileBytes} bayttan büyük.");
        }

        var text = File.ReadAllText(path);
        try
        {
            using var document = JsonDocument.Parse(text);
            return ReadIdentifiers(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new PackageListFormatException("Liste dosyası geçerli bir JSON değil.", ex);
        }
    }

    private static bool SourceIs(PackageInfo package, string name) =>
        string.Equals(package.Source, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownSource(string? source) =>
        string.Equals(source, PackageSources.Winget, StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, PackageSources.MicrosoftStore, StringComparison.OrdinalIgnoreCase);

    private static ExportSource SourceOf(SourceDetailsData details, List<PackageInfo> exportable) => new()
    {
        Packages =
        [
            .. exportable
                .Where(package => SourceIs(package, details.Name))
                .Select(package => new ExportPackage { PackageIdentifier = package.Id }),
        ],
        SourceDetails = details,
    };

    private static List<string> ReadIdentifiers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Sources", out var sources)
            || sources.ValueKind != JsonValueKind.Array)
        {
            throw new PackageListFormatException("Liste dosyasında 'Sources' dizisi yok.");
        }

        var identifiers = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("Packages", out var packages)
                || packages.ValueKind != JsonValueKind.Array)
            {
                throw new PackageListFormatException("Liste dosyasında bir kaynağın 'Packages' dizisi yok.");
            }

            foreach (var package in packages.EnumerateArray())
            {
                var identifier = ReadIdentifier(package);
                if (!seen.Add(identifier))
                {
                    continue;
                }

                if (seen.Count > MaxPackages)
                {
                    throw new PackageListTooLargeException($"Liste dosyasında {MaxPackages} paketten fazla kimlik var.");
                }

                identifiers.Add(identifier);
            }
        }

        return identifiers;
    }

    private static string ReadIdentifier(JsonElement package)
    {
        if (package.ValueKind == JsonValueKind.Object
            && package.TryGetProperty("PackageIdentifier", out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } identifier
            && !string.IsNullOrWhiteSpace(identifier))
        {
            return identifier.Trim();
        }

        throw new PackageListFormatException("Liste dosyasında 'PackageIdentifier' alanı eksik veya boş bir paket var.");
    }

    // Alan adları ve sırası gerçek `winget export` çıktısıyla aynıdır.
    private sealed class ExportFile
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; init; } = SchemaUrl;

        public required string CreationDate { get; init; }

        public required List<ExportSource> Sources { get; init; }

        public required string WinGetVersion { get; init; }
    }

    private sealed class ExportSource
    {
        public required List<ExportPackage> Packages { get; init; }

        public required SourceDetailsData SourceDetails { get; init; }
    }

    private sealed class ExportPackage
    {
        public required string PackageIdentifier { get; init; }
    }

    private sealed record SourceDetailsData(string Argument, string Identifier, string Name, string Type);
}
