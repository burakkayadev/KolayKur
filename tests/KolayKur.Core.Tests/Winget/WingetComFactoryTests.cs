using KolayKur.Core.Winget;
using Microsoft.Management.Deployment;

namespace KolayKur.Core.Tests.Winget;

public class WingetComFactoryTests
{
    public static TheoryData<string, Guid> InterfaceIids => new()
    {
        { "IPackageManager", WingetComFactory.PackageManagerIid },
        { "IFindPackagesOptions", WingetComFactory.FindPackagesOptionsIid },
        { "ICreateCompositePackageCatalogOptions", WingetComFactory.CreateCompositePackageCatalogOptionsIid },
        { "IInstallOptions", WingetComFactory.InstallOptionsIid },
        { "IUninstallOptions", WingetComFactory.UninstallOptionsIid },
        { "IPackageMatchFilter", WingetComFactory.PackageMatchFilterIid },
    };

    [Theory]
    [MemberData(nameof(InterfaceIids))]
    public void Hardcoded_iid_matches_the_projected_interface(string interfaceName, Guid expected)
    {
        // Arayüz türleri projeksiyon derlemesinde internal olduğu için yansıma ile okunur.
        var type = typeof(PackageManager).Assembly.GetType($"Microsoft.Management.Deployment.{interfaceName}");

        Assert.NotNull(type);
        Assert.Equal(expected, type.GUID);
    }
}
