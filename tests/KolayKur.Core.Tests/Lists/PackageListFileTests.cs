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

    private static PackageInfo Pkg(string id, string? source = PackageSources.Winget) =>
        new(id, $"{id} adı", "Yayıncı", "1.0", null, source);

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
    [InlineData("Git.Git", "winget", true)]
    [InlineData("Microsoft.WindowsTerminal", "winget", true)]
    [InlineData("9WZDNCRFJ3TJ", "msstore", true)]
    [InlineData("9WZDNCRFJ3TJ", "MSSTORE", true)]
    [InlineData("Git.Git", "WinGet", true)]
    [InlineData("Git.Git", null, false)]
    [InlineData("Git.Git", "other", false)]
    [InlineData(@"ARP\Machine\X64\Foo", "winget", false)]
    [InlineData(@"ARP\User\X86\Foo 1.0", "msstore", false)]
    [InlineData(@"arp\machine\x64\foo", "winget", false)]
    [InlineData(@"MSIX\Bar_1.0_x64__abc", "winget", false)]
    [InlineData(@"msix\bar_1.0_x64__abc", "msstore", false)]
    [InlineData("", "winget", false)]
    [InlineData("   ", "msstore", false)]
    public void Unknown_source_is_not_exportable(string id, string? source, bool expected)
    {
        Assert.Equal(expected, PackageListFile.IsExportable(Pkg(id, source)));
    }

    [Fact]
    public void Save_groups_sources_ignoring_case()
    {
        var path = Path.Combine(directory, "liste.json");

        PackageListFile.Save(path, [Pkg("9WZDNCRFJ3TJ", "MSSTORE"), Pkg("Git.Git", "WinGet")], "v1.0");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var sources = document.RootElement.GetProperty("Sources").EnumerateArray().ToList();
        Assert.Equal(2, sources.Count);
        Assert.Equal(["Git.Git"], IdsOf(sources[0]));
        Assert.Equal(["9WZDNCRFJ3TJ"], IdsOf(sources[1]));
    }

    [Theory]
    [InlineData("ARPA.Tool")]
    [InlineData("MSIXHero.MSIXHero")]
    public void IsExportable_keeps_real_ids_that_merely_start_with_the_same_letters(string id)
    {
        Assert.True(PackageListFile.IsExportable(Pkg(id)));
    }

    [Fact]
    public void Save_writes_one_source_entry_per_source()
    {
        // Arrange
        var path = Path.Combine(directory, "liste.json");

        // Act: sıra girdi sırasından bağımsız: önce winget, sonra msstore; kaynaksız paket yazılmaz.
        PackageListFile.Save(
            path,
            [Pkg("9WZDNCRFJ3TJ", PackageSources.MicrosoftStore), Pkg("Git.Git"), Pkg("Yok.Kaynak", null), Pkg("7zip.7zip")],
            "1.29.380");

        // Assert
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var sources = document.RootElement.GetProperty("Sources").EnumerateArray().ToList();
        Assert.Equal(2, sources.Count);

        var winget = sources[0].GetProperty("SourceDetails");
        Assert.Equal("winget", winget.GetProperty("Name").GetString());
        Assert.Equal("Microsoft.Winget.Source_8wekyb3d8bbwe", winget.GetProperty("Identifier").GetString());
        Assert.Equal(["Git.Git", "7zip.7zip"], IdsOf(sources[0]));

        var store = sources[1].GetProperty("SourceDetails");
        Assert.Equal("msstore", store.GetProperty("Name").GetString());
        Assert.Equal("StoreEdgeFD", store.GetProperty("Identifier").GetString());
        Assert.Equal("Microsoft.Rest", store.GetProperty("Type").GetString());
        Assert.Equal("https://storeedgefd.dsx.mp.microsoft.com/v9.0", store.GetProperty("Argument").GetString());
        Assert.Equal(["9WZDNCRFJ3TJ"], IdsOf(sources[1]));
    }

    [Fact]
    public void Save_writes_only_the_sources_that_have_packages()
    {
        var path = Path.Combine(directory, "liste.json");

        PackageListFile.Save(path, [Pkg("9WZDNCRFJ3TJ", PackageSources.MicrosoftStore)], "1.29.380");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var source = document.RootElement.GetProperty("Sources").EnumerateArray().Single();
        Assert.Equal("msstore", source.GetProperty("SourceDetails").GetProperty("Name").GetString());
    }

    [Fact]
    public void Load_reads_ids_from_all_sources()
    {
        var path = Path.Combine(directory, "liste.json");
        PackageListFile.Save(path, [Pkg("Git.Git"), Pkg("9WZDNCRFJ3TJ", PackageSources.MicrosoftStore)], "1.29.380");

        Assert.Equal(["Git.Git", "9WZDNCRFJ3TJ"], PackageListFile.Load(path));
    }

    [Fact]
    public void Load_still_reads_old_single_source_files()
    {
        var sample = Path.Combine(AppContext.BaseDirectory, "Samples", "winget-export.json");

        Assert.Equal(3, PackageListFile.Load(sample).Count);
    }

    private static List<string?> IdsOf(JsonElement source) =>
        [.. source.GetProperty("Packages").EnumerateArray().Select(p => p.GetProperty("PackageIdentifier").GetString())];

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
