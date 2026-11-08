using System.IO.Compression;
using KolayKur.Core.Repair;

namespace KolayKur.Core.Tests.Repair;

public sealed class WindowsRepairActionsTests : IDisposable
{
    // microsoft/winget-cli releases/latest yanıtından kısaltılmış örnek (gerçek alan adları ve değerler).
    private const string ReleaseJson = """
        {
          "tag_name": "v1.29.380",
          "assets": [
            {
              "name": "DesktopAppInstallerPolicies.zip",
              "size": 41003,
              "digest": "sha256:a0793f0832ba540a55848deeee271c3686f2149cb4ead7fe72be2417c9f62605",
              "browser_download_url": "https://github.com/microsoft/winget-cli/releases/download/v1.29.380/DesktopAppInstallerPolicies.zip"
            },
            {
              "name": "DesktopAppInstaller_Dependencies.zip",
              "size": 97760717,
              "digest": "sha256:ba875afe9d190f61218985ac0292a99d1db710bf93e13c68944ca9d89f0d82d1",
              "browser_download_url": "https://github.com/microsoft/winget-cli/releases/download/v1.29.380/DesktopAppInstaller_Dependencies.zip"
            },
            {
              "name": "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle",
              "size": 217276577,
              "digest": "sha256:65dea9c01ce08ee7b763366b27c0e651f97db857c11ca9b9c301826c10092f2e",
              "browser_download_url": "https://github.com/microsoft/winget-cli/releases/download/v1.29.380/Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle"
            }
          ]
        }
        """;

    private readonly string root =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public WindowsRepairActionsTests() => Directory.CreateDirectory(root);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void ParseRelease_maps_real_github_fields()
    {
        var release = WindowsRepairActions.ParseRelease(ReleaseJson);

        Assert.Equal("v1.29.380", release.Tag);
        Assert.Equal("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle", release.AppInstaller.Name);
        Assert.Equal(217276577, release.AppInstaller.Size);
        Assert.Equal("65dea9c01ce08ee7b763366b27c0e651f97db857c11ca9b9c301826c10092f2e", release.AppInstaller.Sha256);
        Assert.Equal("DesktopAppInstaller_Dependencies.zip", release.Dependencies.Name);
        Assert.Equal(97760717, release.Dependencies.Size);
        Assert.Equal("ba875afe9d190f61218985ac0292a99d1db710bf93e13c68944ca9d89f0d82d1", release.Dependencies.Sha256);
        Assert.Equal(
            "https://github.com/microsoft/winget-cli/releases/download/v1.29.380/DesktopAppInstaller_Dependencies.zip",
            release.Dependencies.Url.ToString());
    }

    [Fact]
    public void ParseRelease_rejects_asset_without_digest()
    {
        var json = ReleaseJson.Replace(
            "\"digest\": \"sha256:ba875afe9d190f61218985ac0292a99d1db710bf93e13c68944ca9d89f0d82d1\",",
            "\"digest\": null,",
            StringComparison.Ordinal);

        var ex = Assert.Throws<DownloadVerificationException>(() => WindowsRepairActions.ParseRelease(json));

        Assert.Contains("DesktopAppInstaller_Dependencies.zip", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRelease_rejects_missing_asset()
    {
        var json = ReleaseJson.Replace("Microsoft.DesktopAppInstaller_8wekyb3d8bbwe.msixbundle", "baska.msixbundle",
            StringComparison.Ordinal);

        Assert.Throws<DownloadVerificationException>(() => WindowsRepairActions.ParseRelease(json));
    }

    [Fact]
    public void ParseRelease_rejects_download_url_outside_winget_cli_repo()
    {
        var json = ReleaseJson.Replace(
            "https://github.com/microsoft/winget-cli/releases/download/v1.29.380/DesktopAppInstaller_Dependencies.zip",
            "https://example.com/microsoft/winget-cli/releases/download/v1.29.380/DesktopAppInstaller_Dependencies.zip",
            StringComparison.Ordinal);

        Assert.Throws<DownloadVerificationException>(() => WindowsRepairActions.ParseRelease(json));
    }

    private string CreateZip(params string[] entryNames)
    {
        var zipPath = Path.Combine(root, "A B C", "İndirilenler", "deps.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var name in entryNames)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("içerik");
        }

        return zipPath;
    }

    [Fact]
    public void ExtractX64_extracts_only_x64_folder_into_path_with_spaces_and_turkish_letters()
    {
        var zipPath = CreateZip(
            "x64/Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_x64__8wekyb3d8bbwe.appx",
            "x64/Microsoft.WindowsAppRuntime.1.8_8000.616.304.0_x64__8wekyb3d8bbwe.appx",
            "x86/Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_x86__8wekyb3d8bbwe.appx");
        var target = Path.Combine(root, "Çıktı Klasörü");

        var files = WindowsRepairActions.ExtractX64(zipPath, target);

        Assert.Equal(2, files.Count);
        Assert.All(files, file => Assert.True(File.Exists(file)));
        Assert.All(files, file => Assert.StartsWith(target, file, StringComparison.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(Path.Combine(target, "x86")));
    }

    [Fact]
    public void ExtractX64_rejects_entries_that_escape_the_target_folder()
    {
        var zipPath = CreateZip("x64/../../zararli.appx");
        var target = Path.Combine(root, "hedef");

        Assert.Throws<InvalidDataException>(() => WindowsRepairActions.ExtractX64(zipPath, target));

        Assert.False(File.Exists(Path.Combine(root, "zararli.appx")));
    }
}
