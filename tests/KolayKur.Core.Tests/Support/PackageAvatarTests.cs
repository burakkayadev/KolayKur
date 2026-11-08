using System.Globalization;
using KolayKur.Core.Support;

namespace KolayKur.Core.Tests.Support;

// Kültürü geçici değiştirdiği için diğer kültür duyarlı testlerle aynı anda çalışmaz.
[Collection("Localization")]
public sealed class PackageAvatarTests
{
    [Theory]
    [InlineData("7zip.7zip", "7-Zip", "7")]
    [InlineData("VideoLAN.VLC", "  vlc media", "V")]
    [InlineData("x", "çay", "Ç")]
    [InlineData("x", "ılık", "I")]
    [InlineData("x", "--", "?")]
    [InlineData("x", "", "?")]
    public void Letter_is_first_letter_or_digit_uppercased(string id, string name, string letter)
        => Assert.Equal(letter, PackageAvatar.For(id, name).Letter);

    // FNV-1a 32 bit; kimliğin ToUpperInvariant() hâlinin her UTF-16 karakteri tek değer olarak; % 8.
    [Theory]
    [InlineData("Git.Git", 5)]
    [InlineData("7zip.7zip", 1)]
    [InlineData("VideoLAN.VLC", 6)]
    public void Color_index_is_pinned(string id, int index)
        => Assert.Equal(index, PackageAvatar.For(id, "a").ColorIndex);

    [Fact]
    public void Color_index_ignores_case_and_turkish_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var upper = PackageAvatar.For("INTEL.Driver", "a").ColorIndex;
            var lower = PackageAvatar.For("intel.driver", "a").ColorIndex;
            Assert.Equal(1, upper);
            Assert.Equal(upper, lower);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Color_index_stays_within_palette()
    {
        for (var i = 0; i < 200; i++)
        {
            var index = PackageAvatar.For("Pkg.Id" + i, "n").ColorIndex;
            Assert.InRange(index, 0, PackageAvatar.PaletteSize - 1);
        }
    }
}
