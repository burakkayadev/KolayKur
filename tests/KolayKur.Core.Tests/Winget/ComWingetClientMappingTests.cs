using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Winget;

public sealed class ComWingetClientMappingTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("Unknown", null)]
    [InlineData("unknown", null)]
    [InlineData("UNKNOWN", null)]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("Unknown.Edition", "Unknown.Edition")]
    public void VersionOrNull_hides_empty_and_unknown_versions(string? input, string? expected)
    {
        Assert.Equal(expected, ComWingetClient.VersionOrNull(input));
    }
}
