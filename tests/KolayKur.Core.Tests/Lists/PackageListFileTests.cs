using System.Text.Json;
using KolayKur.Core.Lists;
using KolayKur.Core.Models;

namespace KolayKur.Core.Tests.Lists;

public sealed class PackageListFileTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public PackageListFileTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static PackageInfo Pkg(string id) => new(id, $"{id} adı", "Yayıncı", "1.0", null);

    private string FileWith(string content)
    {
        var path = Path.Combine(directory, "liste.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Save_then_Load_returns_same_ids()
    {
        // Arrange
        var path = Path.Combine(directory, "liste.json");

        // Act
        PackageListFile.Save(path, [Pkg("Git.Git"), Pkg("7zip.7zip")], "1.29.380");

        // Assert
        Assert.Equal(["Git.Git", "7zip.7zip"], PackageListFile.Load(path));
    }

    [Fact]
    public void Load_reads_real_winget_export()
    {
        // Arrange: gerçek `winget export` çıktısından kırpılmış örnek (üç paket).
        var sample = Path.Combine(AppContext.BaseDirectory, "Samples", "winget-export.json");

        // Act
        var ids = PackageListFile.Load(sample);

        // Assert
        Assert.Equal(["Git.Git", "7zip.7zip", "Microsoft.WindowsTerminal"], ids);
    }

    [Theory]
    [InlineData("{ bu json değil")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Sources": "yanlış"}""")]
    [InlineData("""{"Sources": [{"Paketler": []}]}""")]
    [InlineData("""{"Sources": [{"Packages": [{"Id": "Git.Git"}]}]}""")]
    [InlineData("""{"Sources": [{"Packages": [{"PackageIdentifier": ""}]}]}""")]
    public void Load_rejects_broken_file(string content)
    {
        var path = FileWith(content);

        Assert.Throws<PackageListFormatException>(() => PackageListFile.Load(path));
    }

    [Fact]
    public void Load_reads_packages_of_every_source()
    {
        var path = FileWith("""
            {"Sources": [
              {"Packages": [{"PackageIdentifier": "A.One"}]},
              {"Packages": [{"PackageIdentifier": "B.Two"}, {"PackageIdentifier": "C.Three"}]}
            ]}
            """);

        Assert.Equal(["A.One", "B.Two", "C.Three"], PackageListFile.Load(path));
    }

    [Fact]
    public void Save_writes_the_winget_export_schema()
    {
        // Arrange
        var path = Path.Combine(directory, "liste.json");

        // Act
        PackageListFile.Save(path, [Pkg("Git.Git")], "1.29.380");

        // Assert: `winget import` bu alanları okur; gerçek dışa aktarımla aynı şema.
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("https://aka.ms/winget-packages.schema.2.0.json", root.GetProperty("$schema").GetString());
        Assert.Equal("1.29.380", root.GetProperty("WinGetVersion").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("CreationDate").ValueKind);

        var source = root.GetProperty("Sources").EnumerateArray().Single();
        var details = source.GetProperty("SourceDetails");
        Assert.Equal("winget", details.GetProperty("Name").GetString());
        Assert.Equal("Microsoft.Winget.Source_8wekyb3d8bbwe", details.GetProperty("Identifier").GetString());
        Assert.Equal("Microsoft.PreIndexed.Package", details.GetProperty("Type").GetString());
        Assert.Equal("https://cdn.winget.microsoft.com/cache", details.GetProperty("Argument").GetString());

        var package = source.GetProperty("Packages").EnumerateArray().Single();
        Assert.Equal("Git.Git", package.GetProperty("PackageIdentifier").GetString());
        Assert.Single(package.EnumerateObject());
    }

    [Fact]
    public void Save_keeps_non_ascii_text_and_leaves_no_temporary_file()
    {
        var path = Path.Combine(directory, "liste.json");

        PackageListFile.Save(path, [Pkg("Çay.Şeker")], "1.29.380");

        Assert.Equal(["Çay.Şeker"], PackageListFile.Load(path));
        Assert.Equal([path], Directory.GetFiles(directory));
    }
}
