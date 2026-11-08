using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

// Strings.resx (varsayılan) Türkçedir; Strings.en.resx İngilizce uydu kaynağıdır.
[assembly: NeutralResourcesLanguage("tr")]

namespace KolayKur.Core.Localization;

/// <summary>
/// Ekran metinlerinin kaynağı. Bağlamalar <c>{Binding [Anahtar], Source={x:Static loc:Loc.Instance}}</c> biçiminde
/// yazılır; dil değişince indeksleyici bildirimi tüm metinleri yeniler.
/// </summary>
public sealed class Loc : ObservableObject
{
    public const string DefaultLanguage = "tr";

    private static readonly string[] SupportedLanguages = [DefaultLanguage, "en"];

    private volatile CultureInfo culture = CultureInfo.GetCultureInfo(DefaultLanguage);

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    internal static ResourceManager Resources { get; } =
        new("KolayKur.Core.Localization.Strings", typeof(Loc).Assembly);

    public string Language => culture.Name;

    /// <summary>Anahtar bulunamazsa, eksik metin gözden kaçmasın diye <c>!Anahtar!</c> döner.</summary>
    public string this[string key] => Resources.GetString(key, culture) ?? $"!{key}!";

    /// <summary>Desteklenmeyen bir dil kodu varsayılan dile (Türkçe) düşer.</summary>
    public void SetLanguage(string code)
    {
        var language = SupportedLanguages.FirstOrDefault(
            supported => string.Equals(supported, code, StringComparison.OrdinalIgnoreCase)) ?? DefaultLanguage;
        if (string.Equals(culture.Name, language, StringComparison.Ordinal))
        {
            return;
        }

        culture = CultureInfo.GetCultureInfo(language);
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged("Item[]");
    }
}
