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
    private const int MaxCachedFileBytes = 4 * 1024 * 1024;
    private const string InstalledSuffix = "#installed";

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
    public bool IsNegative(string packageId, bool installed = false)
    {
        try
        {
            var marker = NegativePath(packageId, installed);
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

            TryDelete(NegativePath(packageId, installed: false));
            TryDelete(NegativePath(packageId, installed: true));
            Trim();
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void PutNegative(string packageId, bool installed = false)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            File.WriteAllBytes(NegativePath(packageId, installed), []);
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

    // "Kurulu değil" ve "kurulu" için ayrı kayıt: kurulumdan önce verilen "simge yok" kurulumdan sonra engel olmaz.
    private string NegativePath(string packageId, bool installed) =>
        Path.Combine(directory, Prefix(installed ? packageId + InstalledSuffix : packageId) + ".none");

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

    /// <summary>Bütün parçaların uzunluk ve CRC denetimi (bkz. <see cref="PngIntegrity"/>) ve en az 16 piksel boyut.</summary>
    internal static bool IsValidPng(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxCachedFileBytes)
            {
                return false;
            }

            return PngIntegrity.Inspect(File.ReadAllBytes(path)) is (int w, int h) && w >= IconLimits.MinPixels && h >= IconLimits.MinPixels;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Önbellek dışındaki yolları yok sayar; bozuk çıkan dosyayı siler ki sonraki istekte yeniden bulunsun.</summary>
    public void Invalidate(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(full);
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
