using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using KolayKur.Core.Icons;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace KolayKur.App.Icons;

/// <summary>
/// Kurulu programın simgesini yalnızca yerel dosya ve kayıttan okur (internet yok, hiçbir şey çalıştırılmaz):
/// MSIX paketlerinde manifestteki logo, masaüstü programlarında kaldırma kaydındaki <c>DisplayIcon</c>,
/// o da yoksa kurulum klasöründeki exe'nin simgesi. Bulamazsa <c>null</c>.
/// </summary>
public sealed partial class WindowsInstalledIconReader : IInstalledIconReader
{
    private const int MaxFileBytes = 4 * 1024 * 1024;
    private const int PreferredExtractSize = 256;
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    private static readonly string[] SkippedExeWords = ["unins", "uninst", "setup", "install", "update", "crash", "helper", "report", "vc_redist"];

    public Task<byte[]?> ReadAsync(string installedIconSource, CancellationToken ct) =>
        Task.Run(() => Read(installedIconSource, ct), ct);

    private static byte[]? Read(string source, CancellationToken ct)
    {
        try
        {
            foreach (var family in InstalledIconSource.Values(source, "pfn"))
            {
                ct.ThrowIfCancellationRequested();
                var logo = ReadPackageLogo(family);
                if (logo is not null)
                {
                    return logo;
                }
            }

            var location = InstalledIconSource.Values(source, "location").FirstOrDefault();
            foreach (var product in InstalledIconSource.Values(source, "product"))
            {
                ct.ThrowIfCancellationRequested();
                var fromRegistry = ReadFromUninstallKey(product, ref location);
                if (fromRegistry is not null)
                {
                    return fromRegistry;
                }
            }

            return location is null ? null : ReadFromFolder(location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException
            or System.Security.SecurityException or System.Xml.XmlException or ArgumentException or NotSupportedException)
        {
            // Yerel simge okunamadı: sıradaki kaynağa geçilir.
            return null;
        }
    }

    private static byte[]? ReadFromUninstallKey(string productCode, ref string? location)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var root in UninstallRoots)
            {
                using var key = hive.OpenSubKey($@"{root}\{productCode}");
                if (key is null)
                {
                    continue;
                }

                location ??= key.GetValue("InstallLocation") as string;
                if (key.GetValue("DisplayIcon") is string displayIcon && ParseIconReference(displayIcon) is var (path, index)
                    && File.Exists(path))
                {
                    var bytes = IconFromFile(path, index);
                    if (bytes is not null)
                    {
                        return bytes;
                    }
                }
            }
        }

        return null;
    }

    /// <summary><c>"C:\a b\x.exe",0</c> ya da <c>C:\x.ico</c> biçimlerini çözer.</summary>
    internal static (string Path, int Index)? ParseIconReference(string value)
    {
        var text = Environment.ExpandEnvironmentVariables(value.Trim());
        if (text.Length == 0)
        {
            return null;
        }

        var index = 0;
        string path;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            if (end < 0)
            {
                return null;
            }

            path = text[1..end];
            var rest = text[(end + 1)..].TrimStart(',', ' ');
            _ = int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
        }
        else
        {
            var comma = text.LastIndexOf(',');
            if (comma > 0 && int.TryParse(text[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                path = text[..comma];
            }
            else
            {
                path = text;
                index = 0;
            }
        }

        return Path.IsPathFullyQualified(path) && !IsNetworkPath(path) ? (path, index) : null;
    }

    /// <summary>UNC (<c>\sunucupaylaşım</c>) ve cihaz (<c>\?</c>) yolları ağa çıkabilir; yerel dosya dışında bir şey okunmaz.</summary>
    internal static bool IsNetworkPath(string path) => path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// Birden çok programın paylaştığı klasörler (sürücü kökü, Windows, System32, Program Files'ın kendisi, ortak dosyalar, ProgramData):
    /// buradaki "en büyük exe" uygulamanın değildir, simge olarak alınmaz.
    /// </summary>
    internal static bool IsSharedFolder(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(full, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var shared = new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.Desktop,
        };
        return shared.Select(Environment.GetFolderPath)
            .Where(p => p.Length > 0)
            .Any(p => string.Equals(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), full, StringComparison.OrdinalIgnoreCase));
    }

    private static byte[]? ReadFromFolder(string folder)
    {
        if (IsNetworkPath(folder) || !Path.IsPathFullyQualified(folder) || IsSharedFolder(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var exe = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly)
            .Where(f => !SkippedExeWords.Any(w => Path.GetFileName(f).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
        return exe is null ? null : IconFromFile(exe, 0);
    }

    private static byte[]? IconFromFile(string path, int index)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) || extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return new FileInfo(path).Length is > 0 and <= MaxFileBytes ? File.ReadAllBytes(path) : null;
        }

        return ExtractFromModule(path, index);
    }

    /// <summary>exe/dll içindeki simgeyi (dosya yalnızca veri olarak okunur) PNG olarak çıkarır.</summary>
    private static byte[]? ExtractFromModule(string path, int index)
    {
        foreach (var size in new[] { PreferredExtractSize, 128, 48, 32 })
        {
            var handles = new IntPtr[1];
            var ids = new uint[1];
            var count = PrivateExtractIcons(path, index, size, size, handles, ids, 1, 0);
            if (count == 0 || handles[0] == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            finally
            {
                _ = DestroyIcon(handles[0]);
            }
        }

        return null;
    }

    private static byte[]? ReadPackageLogo(string packageFamilyName)
    {
        var packages = new PackageManager().FindPackagesForUser(string.Empty, packageFamilyName);
        foreach (var package in packages)
        {
            var root = package.InstalledPath;
            var manifest = Path.Combine(root, "AppxManifest.xml");
            if (!File.Exists(manifest))
            {
                continue;
            }

            var logo = ManifestLogo(XDocument.Load(manifest));
            var file = logo is null ? null : BestLogoFile(root, logo);
            if (file is not null)
            {
                return File.ReadAllBytes(file);
            }
        }

        return null;
    }

    private static string? ManifestLogo(XDocument manifest)
    {
        foreach (var attribute in new[] { "Square44x44Logo", "Square150x150Logo" })
        {
            var value = manifest.Descendants().Where(e => e.Name.LocalName == "VisualElements").Select(e => (string?)e.Attribute(attribute)).FirstOrDefault(v => v is not null);
            if (value is not null)
            {
                return value;
            }
        }

        return manifest.Descendants().Where(e => e.Name.LocalName == "Logo").Select(e => e.Value).FirstOrDefault();
    }

    /// <summary>Manifestteki temel ada (<c>Assets\Logo.png</c>) uyan ölçek/boyut varyantlarından en büyüğünü (256 pikseli geçmeyen) seçer.</summary>
    private static string? BestLogoFile(string root, string logo)
    {
        var relative = logo.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(full)!;
        var stem = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        if (!Directory.Exists(directory))
        {
            return File.Exists(full) ? full : null;
        }

        return Directory.EnumerateFiles(directory, stem + "*" + extension)
            .Where(f => !f.Contains("contrast-", StringComparison.OrdinalIgnoreCase))
            .Select(f => (File: f, Pixels: EffectivePixels(Path.GetFileName(f))))
            .Where(c => c.Pixels <= PreferredExtractSize)
            .OrderByDescending(c => c.Pixels)
            .ThenByDescending(c => c.File.Contains("unplated", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.File)
            .FirstOrDefault();
    }

    private static int EffectivePixels(string fileName)
    {
        var target = TargetSize().Match(fileName);
        if (target.Success)
        {
            return int.Parse(target.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        var scale = Scale().Match(fileName);
        return scale.Success ? int.Parse(scale.Groups[1].Value, CultureInfo.InvariantCulture) * 44 / 100 : 44;
    }

    [GeneratedRegex(@"targetsize-(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex TargetSize();

    [GeneratedRegex(@"scale-(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Scale();

    [DllImport("user32.dll", EntryPoint = "PrivateExtractIconsW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PrivateExtractIcons(
        string fileName, int iconIndex, int cx, int cy, [Out] IntPtr[] icons, [Out] uint[] iconIds, uint iconCount, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
