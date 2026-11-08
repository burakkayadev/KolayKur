using System.Security.Cryptography;
using KolayKur.Core.Repair;

namespace KolayKur.Core.Tests.Repair;

public sealed class DownloadVerifierTests : IDisposable
{
    private readonly string root =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public DownloadVerifierTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private string WriteFile(string relativePath, byte[] bytes)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Verify_deletes_truncated_file()
    {
        var bytes = new byte[10];
        var path = WriteFile("dosya.zip", bytes);

        Assert.Throws<DownloadVerificationException>(() => DownloadVerifier.Verify(path, 20, Sha256Of(bytes)));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Verify_deletes_wrong_hash()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var path = WriteFile("dosya.zip", bytes);

        Assert.Throws<DownloadVerificationException>(
            () => DownloadVerifier.Verify(path, bytes.Length, Sha256Of([9, 9, 9, 9])));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Verify_accepts_uppercase_or_lowercase_hash()
    {
        var bytes = new byte[] { 5, 6, 7 };
        var path = WriteFile("dosya.zip", bytes);

        DownloadVerifier.Verify(path, bytes.Length, Sha256Of(bytes).ToLowerInvariant());

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(0, "AA")]
    [InlineData(4, "")]
    public void Verify_deletes_file_when_size_or_hash_is_unknown(long size, string hash)
    {
        var path = WriteFile("dosya.zip", [1, 2, 3, 4]);

        Assert.Throws<DownloadVerificationException>(() => DownloadVerifier.Verify(path, size, hash));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Verify_works_with_spaces_and_turkish_letters()
    {
        var bytes = new byte[] { 10, 20, 30, 40, 50 };
        var path = WriteFile(Path.Combine("A B C", "İndirilenler", "dosya.zip"), bytes);

        DownloadVerifier.Verify(path, bytes.Length, Sha256Of(bytes));

        Assert.True(File.Exists(path));
    }
}
