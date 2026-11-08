using System.Runtime.InteropServices;
using Microsoft.Management.Deployment;
using WinRT;

namespace KolayKur.Core.Winget;

/// <summary>
/// winget COM nesnelerini dış süreçte (CLSCTX_LOCAL_SERVER) oluşturur.
/// CLSID'ler winget-cli deposundaki ClassesDefinition.cs dosyasının OutOfProc (kararlı sürüm) değerleridir.
/// </summary>
public sealed partial class WingetComFactory
{
    private const uint ClsctxLocalServer = 0x4;

    // APPMODEL_ERROR_NO_PACKAGE: winget yoksa da, .winmd dosyası çıktı klasöründe eksikse de bu kodla başarısız olunur.
    private const int AppModelErrorNoPackage = unchecked((int)0x80073D54);
    private const string WinmdFileName = "Microsoft.Management.Deployment.winmd";

    private static readonly Guid PackageManagerClsid = new("C53A4F16-787E-42A4-B304-29EFFB4BF597");
    private static readonly Guid FindPackagesOptionsClsid = new("572DED96-9C60-4526-8F92-EE7D91D38C1A");
    private static readonly Guid CreateCompositePackageCatalogOptionsClsid = new("526534B8-7E46-47C8-8416-B1685C327D37");
    private static readonly Guid InstallOptionsClsid = new("1095F097-EB96-453B-B4E6-1613637F3B14");
    private static readonly Guid UninstallOptionsClsid = new("E1D9A11E-9F85-4D87-9C17-2B93143ADB8D");
    private static readonly Guid PackageMatchFilterClsid = new("D02C9DAF-99DC-429C-B503-4E504E4AB000");

    // Varsayılan arayüzlerin IID değerleri (Microsoft.Management.Deployment.winmd). Arayüz türleri
    // projeksiyonda internal olduğundan sabit yazılır; tests/KolayKur.Core.Tests/Winget/WingetComFactoryTests.cs eşleşmeyi doğrular.
    internal static readonly Guid PackageManagerIid = new("b375e3b9-f2e0-5c93-87a7-b67497f7e593");
    internal static readonly Guid FindPackagesOptionsIid = new("a5270edd-7da7-57a3-bace-f2593553561f");
    internal static readonly Guid CreateCompositePackageCatalogOptionsIid = new("21abaa76-089d-51c5-a745-c85eefe70116");
    internal static readonly Guid InstallOptionsIid = new("6ee9db69-ab48-5e72-a474-33a924cd23b3");
    internal static readonly Guid UninstallOptionsIid = new("3ebc67f0-8339-594b-8a42-f90b69d02bbe");
    internal static readonly Guid PackageMatchFilterIid = new("d981eca3-4de5-5ad7-967a-698c7d60fc3b");

    public PackageManager CreatePackageManager() =>
        Create<PackageManager>(PackageManagerClsid, PackageManagerIid);

    public FindPackagesOptions CreateFindPackagesOptions() =>
        Create<FindPackagesOptions>(FindPackagesOptionsClsid, FindPackagesOptionsIid);

    public CreateCompositePackageCatalogOptions CreateCompositeOptions() =>
        Create<CreateCompositePackageCatalogOptions>(
            CreateCompositePackageCatalogOptionsClsid, CreateCompositePackageCatalogOptionsIid);

    public InstallOptions CreateInstallOptions() =>
        Create<InstallOptions>(InstallOptionsClsid, InstallOptionsIid);

    public UninstallOptions CreateUninstallOptions() =>
        Create<UninstallOptions>(UninstallOptionsClsid, UninstallOptionsIid);

    public PackageMatchFilter CreateMatchFilter() =>
        Create<PackageMatchFilter>(PackageMatchFilterClsid, PackageMatchFilterIid);

    /// <summary>Winmd eksikse etkinleştirmenin neden başarısız olduğunu açıklayan özel durum; değilse <c>null</c>.</summary>
    internal static Exception? ExplainActivationFailure(COMException failure, string baseDirectory)
    {
        if (failure.HResult != AppModelErrorNoPackage
            || File.Exists(Path.Combine(baseDirectory, WinmdFileName)))
        {
            return null;
        }

        return new FileNotFoundException(
            $"{WinmdFileName} dosyası {baseDirectory} klasöründe yok; winget nesneleri bu dosya olmadan oluşturulamaz. "
            + "Dosya, programın yanına kopyalanmalıdır (winget'in kendisinin eksik olmasından farklı bir sorundur).",
            failure);
    }

    private static T Create<T>(Guid clsid, Guid iid)
    {
        try
        {
            var hr = CoCreateInstance(in clsid, IntPtr.Zero, ClsctxLocalServer, in iid, out var instance);
            Marshal.ThrowExceptionForHR(hr);
            return MarshalGeneric<T>.FromAbi(instance);
        }
        catch (COMException ex)
        {
            var explained = ExplainActivationFailure(ex, AppContext.BaseDirectory);
            if (explained is null)
            {
                throw;
            }

            throw explained;
        }
    }

    [LibraryImport("api-ms-win-core-com-l1-1-0.dll")]
    private static partial int CoCreateInstance(
        in Guid clsid, IntPtr outer, uint clsContext, in Guid iid, out IntPtr instance);
}
