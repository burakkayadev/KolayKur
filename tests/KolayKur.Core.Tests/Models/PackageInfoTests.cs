using KolayKur.Core.Localization;
using KolayKur.Core.Models;

namespace KolayKur.Core.Tests.Models;

[Collection("Localization")]
public sealed class PackageInfoTests
{
    [Fact]
    public void Accessible_label_adds_source_for_store_packages_only()
    {
        var store = new PackageInfo("9NXQXXLFST89", "Netflix", "Netflix", null, "1.0", "MSSTORE");
        var winget = new PackageInfo("Git.Git", "Git", "Git", null, "1.0", PackageSources.Winget);
        var none = new PackageInfo("X.Y", "X", "", null, null);

        Assert.Equal($"Netflix ({Loc.Instance["Source_MicrosoftStore"]})", store.AccessibleLabel);
        Assert.Equal("Git", winget.AccessibleLabel);
        Assert.Equal("X", none.AccessibleLabel);
    }
}
