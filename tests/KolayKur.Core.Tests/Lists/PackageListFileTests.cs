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

    [Theory]
    [InlineData("Git.Git", true)]
    [InlineData("7zip.7zip", true)]
    [InlineData("Microsoft.WindowsTerminal", true)]
    [InlineData(@"ARP\Machine\X64\Foo", false)]
    [InlineData(@"ARP\User\X86\Foo 1.0", false)]
    [InlineData(@"arp\machine\x64\foo", false)]
    [InlineData(@"MSIX\Bar_1.0_x64__abc", false)]
    [InlineData(@"msix\bar_1.0_x64__abc", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsExportable_excludes_ids_without_a_winget_source(string id, bool expected)
    {
        Assert.Equal(expected, PackageListFile.IsExportable(id));
    }

    [Theory]
    [InlineData("ARPA.Tool")]
    [InlineData("MSIXHero.MSIXHero")]
    public void IsExportable_keeps_real_ids_that_merely_start_with_the_same_letters(string id)
    {
        Assert.True(PackageListFile.IsExportable(id));
    }

    [Fact]
    public void Save_of_an_empty_list_writes_a_valid_file_that_loads_as_empty()
    {
        var path = Path.Combine(directory, "liste.json");

        PackageListFile.Save(path, [], "1.29.380");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var source = document.RootElement.GetProperty("Sources").EnumerateArray().Single();
        Assert.Empty(source.GetProperty("Packages").EnumerateArray());
        Assert.Empty(PackageListFile.Load(path));
    }

    [Fact]
    public void Load_trims_ids_and_drops_case_insensitive_duplicates_keeping_first_order()
    {
        var path = FileWith("""
            {"Sources": [
              {"Packages": [
                {"PackageIdentifier": "  Git.Git "},
                {"PackageIdentifier": "git.git"},
                {"PackageIdentifier": "7zip.7zip\t"}]},
              {"Packages": [
                {"PackageIdentifier": "GIT.GIT"},
                {"PackageIdentifier": "VideoLAN.VLC"}]}]}
            """);

        Assert.Equal(["Git.Git", "7zip.7zip", "VideoLAN.VLC"], PackageListFile.Load(path));
    }

    [Fact]
    public void Load_rejects_a_file_larger_than_one_megabyte_without_reading_it()
    {
        var path = FileWith(new string(' ', PackageListFile.MaxFileBytes + 1));

        var ex = Assert.Throws<PackageListTooLargeException>(() => PackageListFile.Load(path));

        Assert.IsAssignableFrom<PackageListFormatException>(ex);
    }

    [Fact]
    public void Load_rejects_more_than_the_maximum_number_of_distinct_ids()
    {
        var path = FileWith(ListOf(PackageListFile.MaxPackages + 1));

        Assert.Throws<PackageListTooLargeException>(() => PackageListFile.Load(path));
    }

    [Fact]
    public void Load_accepts_exactly_the_maximum_and_does_not_count_duplicates()
    {
        var exact = FileWith(ListOf(PackageListFile.MaxPackages));
        Assert.Equal(PackageListFile.MaxPackages, PackageListFile.Load(exact).Count);

        var withDuplicates = FileWith(ListOf(PackageListFile.MaxPackages, repeatFirst: 50));
        Assert.Equal(PackageListFile.MaxPackages, PackageListFile.Load(withDuplicates).Count);
    }

    private static string ListOf(int distinct, int repeatFirst = 0)
    {
        var ids = Enumerable.Range(0, distinct).Select(i => $"Vendor.App{i}")
            .Concat(Enumerable.Repeat("Vendor.App0", repeatFirst));
        return "{\"Sources\": [{\"Packages\": ["
            + string.Join(",", ids.Select(id => $"{{\"PackageIdentifier\": \"{id}\"}}"))
            + "]}]}";
    }
}
