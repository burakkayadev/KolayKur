using System.Collections;
using System.Globalization;
using KolayKur.Core.Localization;

namespace KolayKur.Core.Tests.Localization;

// Loc.Instance süreçte tektir; dili değiştiren testler (ve kabuk testleri) aynı koleksiyonda sırayla çalışır.
[Collection("Localization")]
public sealed class StringsTests
{
    private static HashSet<string> Keys(CultureInfo culture, bool tryParents)
    {
        var set = Loc.Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: tryParents)
            ?? throw new InvalidOperationException($"{culture.Name} kaynakları bulunamadı.");
        return set.Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_key_has_english_text()
    {
        var turkish = Keys(CultureInfo.GetCultureInfo("tr"), tryParents: true);
        var english = Keys(CultureInfo.GetCultureInfo("en"), tryParents: false);

        Assert.NotEmpty(turkish);
        Assert.Empty(turkish.Except(english));
        Assert.Empty(english.Except(turkish));
    }

    [Fact]
    public void No_text_is_empty_in_any_language()
    {
        foreach (var code in new[] { "tr", "en" })
        {
            var culture = CultureInfo.GetCultureInfo(code);
            foreach (var key in Keys(culture, tryParents: code == "tr"))
            {
                Assert.False(string.IsNullOrWhiteSpace(Loc.Resources.GetString(key, culture)), $"{code}: {key}");
            }
        }
    }

    [Fact]
    public void Language_change_updates_text()
    {
        var loc = Loc.Instance;
        var changed = new List<string?>();
        void Record(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => changed.Add(e.PropertyName);
        loc.PropertyChanged += Record;
        try
        {
            loc.SetLanguage("tr");
            Assert.Equal("Ara", loc["Menu_Search"]);

            loc.SetLanguage("en");

            Assert.Equal("Search", loc["Menu_Search"]);
            Assert.Contains("Item[]", changed);
        }
        finally
        {
            loc.PropertyChanged -= Record;
            loc.SetLanguage("tr");
        }
    }

    [Fact]
    public void Unknown_language_falls_back_to_turkish()
    {
        var loc = Loc.Instance;
        try
        {
            loc.SetLanguage("en");

            loc.SetLanguage("xx-not-a-language");

            Assert.Equal("Ara", loc["Menu_Search"]);
        }
        finally
        {
            loc.SetLanguage("tr");
        }
    }

    [Fact]
    public void Missing_key_is_visible_not_empty()
    {
        Assert.Equal("!Yok_Boyle_Anahtar!", Loc.Instance["Yok_Boyle_Anahtar"]);
    }
}
