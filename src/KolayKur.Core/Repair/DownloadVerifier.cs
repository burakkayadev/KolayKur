using System.Security.Cryptography;

namespace KolayKur.Core.Repair;

public sealed class DownloadVerificationException(string message) : Exception(message);

public static class DownloadVerifier
{
    /// <summary>
    /// Boyutu ve SHA-256 özetini doğrular. Doğrulanamayan dosya silinir ve kurulmaz.
    /// </summary>
    public static void Verify(string path, long expectedSize, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            throw new DownloadVerificationException($"İndirilen dosya bulunamadı: {path}");
        }

        var problem = FindProblem(path, expectedSize, expectedSha256);
        if (problem is null)
        {
            return;
        }

        File.Delete(path);
        throw new DownloadVerificationException($"{Path.GetFileName(path)} doğrulanamadı ve silindi: {problem}");
    }

    private static string? FindProblem(string path, long expectedSize, string expectedSha256)
    {
        if (expectedSize <= 0 || string.IsNullOrWhiteSpace(expectedSha256))
        {
            return "beklenen boyut veya SHA-256 özeti bilinmiyor.";
        }

        var actualSize = new FileInfo(path).Length;
        if (actualSize != expectedSize)
        {
            return $"boyut {actualSize} bayt, beklenen {expectedSize} bayt.";
        }

        using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? null
            : "SHA-256 özeti eşleşmiyor.";
    }
}
