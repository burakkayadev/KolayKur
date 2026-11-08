using System.Runtime.InteropServices;
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

    private const int AppModelErrorNoPackage = unchecked((int)0x80073D54);

    [Fact]
    public void Missing_winmd_is_reported_by_name_when_activation_fails_with_no_package()
    {
        var directory = Directory.CreateTempSubdirectory("kolaykur-winmd-").FullName;
        try
        {
            var failure = new COMException("QueryInterface", AppModelErrorNoPackage);

            var explained = WingetComFactory.ExplainActivationFailure(failure, directory);

            Assert.NotNull(explained);
            Assert.Contains("Microsoft.Management.Deployment.winmd", explained.Message);
            Assert.Same(failure, explained.InnerException);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Present_winmd_or_other_error_is_not_explained()
    {
        var directory = Directory.CreateTempSubdirectory("kolaykur-winmd-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Microsoft.Management.Deployment.winmd"), "");

            var winmdPresent = WingetComFactory.ExplainActivationFailure(
                new COMException("QueryInterface", AppModelErrorNoPackage), directory);
            var otherError = WingetComFactory.ExplainActivationFailure(
                new COMException("other", unchecked((int)0x80004005)), Path.Combine(directory, "yok"));

            Assert.Null(winmdPresent);
            Assert.Null(otherError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
