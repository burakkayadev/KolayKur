using System.Security.Cryptography;
using System.Text;

namespace KolayKur.Core.Icons;

/// <summary>
/// Diskteki simge önbelleği. Dosya adı: <c>{temizlenmiş kimlik}~{kimlik özeti}-{kaynak özeti}.png</c>;
/// "simge yok" sonucu <c>{temizlenmiş kimlik}~{kimlik özeti}.none</c> boş dosyasıdır (<see cref="IconLimits.NegativeTtl"/> geçerli).
/// Toplam boyut <c>maxBytes</c>'ı aşarsa en eski dosyalar silinir. Bozuk dosya yok sayılır ve silinir.
/// Bütün G/Ç hataları yutulur: önbellek yoksa ya da yazılamazsa simge yalnızca her seferinde yeniden bulunur.
/// </summary>
public sealed class IconCache(string directory, long maxBytes = IconLimits.MaxCacheBytes, TimeProvider? time = null)
{
    private const int MaxIdChars = 80;
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngEnd = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
    private const int PngMinLength = 8 + 25 + 12;

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object trimGate = new();

    public string Directory => directory;

    /// <summary>Geçerli bir önbellek dosyası varsa yolunu, yoksa <c>null</c> döner (bozuk dosya silinir).</summary>
    public string? TryGet(string packageId)
    {
        try
        {
            if (!System.IO.Directory.Exists(directory))
            {
                return null;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(directory, Prefix(packageId) + "-*.png")
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (IsValidPng(file))
                {
                    return file;
                }

                TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>"Simge yok" kaydı 7 günden yeniyse <c>true</c>.</summary>
    public bool IsNegative(string packageId)
    {
        try
        {
            var marker = NegativePath(packageId);
            if (!File.Exists(marker))
            {
                return false;
            }

            if (clock.GetUtcNow() - File.GetLastWriteTimeUtc(marker) < IconLimits.NegativeTtl)
            {
                return true;
            }

            TryDelete(marker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    /// <summary>PNG'yi yazar (önceki dosyaların yerine geçer) ve yolunu döner; yazılamazsa <c>null</c>.</summary>
    public string? Put(string packageId, string source, byte[] png)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{Prefix(packageId)}-{Hash(source)[..16]}.png");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, png);
            File.Move(temp, path, overwrite: true);
            foreach (var old in System.IO.Directory.EnumerateFiles(directory, Prefix(packageId) + "-*.png").Where(f => f != path))
            {
                TryDelete(old);
            }

            TryDelete(NegativePath(packageId));
            Trim();
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void PutNegative(string packageId)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            File.WriteAllBytes(NegativePath(packageId), []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Önbellek yazılamıyorsa yalnızca bir sonraki istekte yeniden aranır.
        }
    }

    /// <summary>Toplam boyut sınırı aşıldıysa en eski dosyaları siler.</summary>
    public void Trim()
    {
        lock (trimGate)
        {
            try
            {
                var files = new DirectoryInfo(directory).EnumerateFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
                var total = files.Sum(f => f.Length);
                foreach (var file in files)
                {
                    if (total <= maxBytes)
                    {
                        break;
                    }

                    total -= file.Length;
                    TryDelete(file.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Silinemeyenler bir sonraki sefere kalır.
            }
        }
    }

    private string NegativePath(string packageId) => Path.Combine(directory, Prefix(packageId) + ".none");

    private static string Prefix(string packageId)
    {
        var clean = new StringBuilder(Math.Min(packageId.Length, MaxIdChars));
        foreach (var c in packageId.Take(MaxIdChars))
        {
            clean.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' ? c : '_');
        }

        return $"{clean}~{Hash(packageId)[..8]}";
    }

    private static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Başlık, uç parçası ve en az 16 piksel boyut denetimi (yarım kalmış ya da bozulmuş dosyayı eler).</summary>
    internal static bool IsValidPng(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < PngMinLength)
            {
                return false;
            }

            var head = new byte[24];
            stream.ReadExactly(head);
            stream.Seek(-PngEnd.Length, SeekOrigin.End);
            var tail = new byte[PngEnd.Length];
            stream.ReadExactly(tail);
            var width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            var height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return head.AsSpan(0, 8).SequenceEqual(PngSignature) && tail.AsSpan().SequenceEqual(PngEnd)
                && width >= IconLimits.MinPixels && height >= IconLimits.MinPixels;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kullanımdaki dosya bir sonraki sefere kalır.
        }
    }
}
