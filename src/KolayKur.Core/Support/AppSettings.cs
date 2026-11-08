using System.Text.Json;
using System.Text.Json.Serialization;

namespace KolayKur.Core.Support;

public sealed record AppSettings(
    string Language = "tr",
    bool LicenseNoticeShown = false,
    [property: JsonConverter(typeof(AppThemeJsonConverter))] AppTheme Theme = AppTheme.System)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Ayar dosyasını okur. Dosya yoksa, okunamıyorsa veya bozuksa varsayılan ayarlar döner; ayar dosyası yüzünden
    /// program açılmaz hâle gelmemelidir.
    /// </summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            if (loaded is null)
            {
                return new AppSettings();
            }

            return string.IsNullOrWhiteSpace(loaded.Language) ? loaded with { Language = new AppSettings().Language } : loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Klasörü oluşturur; önce geçici dosyaya yazıp asıl dosyanın yerine koyar, yarım dosya kalmaz.</summary>
    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException("Ayar dosyasının klasörü belirlenemedi.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }
}
