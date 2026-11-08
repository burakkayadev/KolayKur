using System.Text.Json;
using System.Text.Json.Serialization;

namespace KolayKur.Core.Support;

/// <summary>Arayüz teması: Windows'un ayarını izle, hep açık ya da hep koyu.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Temayı ayar dosyasına metin olarak yazar. Bilinmeyen metin, sayı ya da null <see cref="AppTheme.System"/> sayılır;
/// böylece bozuk bir tema değeri dil ve lisans notu gibi diğer ayarları kaybettirmez.
/// </summary>
public sealed class AppThemeJsonConverter : JsonConverter<AppTheme>
{
    public override AppTheme Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<AppTheme>(reader.GetString(), ignoreCase: false, out var theme)
            && Enum.IsDefined(theme))
        {
            return theme;
        }

        // Sayı, nesne ya da dizi olabilir; değerin tamamı atlanır ki okuyucu sonraki özellikte kalsın.
        reader.Skip();
        return AppTheme.System;
    }

    public override void Write(Utf8JsonWriter writer, AppTheme value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
