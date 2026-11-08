using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KolayKur.Core.Models;

namespace KolayKur.Core.Lists;

/// <summary>Paket listesi dosyası okunamıyor: JSON bozuk veya <c>winget export</c> şemasına uymuyor.</summary>
public sealed class PackageListFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Paket listesi dosyası. Biçim, <c>winget export</c> çıktısının şemasıdır (<c>winget import -i dosya</c> bunu okur):
/// <c>Sources[].Packages[].PackageIdentifier</c>. Dosyayı <c>winget export</c> da yazmış olabilir; yalnızca kimlikler okunur.
/// </summary>
public static class PackageListFile
{
    private const string SchemaUrl = "https://aka.ms/winget-packages.schema.2.0.json";
    private const string SourceArgument = "https://cdn.winget.microsoft.com/cache";
    private const string SourceIdentifier = "Microsoft.Winget.Source_8wekyb3d8bbwe";
    private const string SourceName = "winget";
    private const string SourceType = "Microsoft.PreIndexed.Package";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Kimlikleri tek kaynaklı (winget) bir liste olarak yazar. Önce geçici dosyaya yazıp asıl dosyanın yerine koyar,
    /// yarım dosya kalmaz. G/Ç hataları (<see cref="IOException"/> vb.) çağırana yansır.
    /// </summary>
    public static void Save(string path, IEnumerable<PackageInfo> packages, string wingetVersion)
    {
        ArgumentNullException.ThrowIfNull(packages);

        var export = new ExportFile
        {
            CreationDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'-00:00'", CultureInfo.InvariantCulture),
            Sources =
            [
                new ExportSource
                {
                    Packages = [.. packages.Select(package => new ExportPackage { PackageIdentifier = package.Id })],
                },
            ],
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
    /// Dosyadaki tüm kaynakların paket kimliklerini sırasıyla döndürür.
    /// Geçersiz JSON veya şemaya uymayan içerik <see cref="PackageListFormatException"/> atar; dosya okunamıyorsa
    /// <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> çağırana yansır.
    /// </summary>
    public static IReadOnlyList<string> Load(string path)
    {
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

    private static List<string> ReadIdentifiers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Sources", out var sources)
            || sources.ValueKind != JsonValueKind.Array)
        {
            throw new PackageListFormatException("Liste dosyasında 'Sources' dizisi yok.");
        }

        var identifiers = new List<string>();
        foreach (var source in sources.EnumerateArray())
        {
            if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("Packages", out var packages)
                || packages.ValueKind != JsonValueKind.Array)
            {
                throw new PackageListFormatException("Liste dosyasında bir kaynağın 'Packages' dizisi yok.");
            }

            foreach (var package in packages.EnumerateArray())
            {
                identifiers.Add(ReadIdentifier(package));
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
            return identifier;
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

        public SourceDetailsData SourceDetails { get; init; } = new();
    }

    private sealed class ExportPackage
    {
        public required string PackageIdentifier { get; init; }
    }

    private sealed class SourceDetailsData
    {
        public string Argument { get; init; } = SourceArgument;

        public string Identifier { get; init; } = SourceIdentifier;

        public string Name { get; init; } = SourceName;

        public string Type { get; init; } = SourceType;
    }
}
