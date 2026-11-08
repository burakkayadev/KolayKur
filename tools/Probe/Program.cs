using KolayKur.Core.Winget;
using Microsoft.Management.Deployment;

const int ResultLimit = 5;

if (args.Length < 2 || !string.Equals(args[0], "search", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Kullanım: Probe search <metin>");
    return 2;
}

try
{
    var factory = new WingetComFactory();
    var manager = factory.CreatePackageManager();

    var catalogRef = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
    var connect = await catalogRef.ConnectAsync();
    if (connect.Status != ConnectResultStatus.Ok)
    {
        Console.Error.WriteLine($"Katalog bağlantısı başarısız: {connect.Status}");
        return 1;
    }

    var filter = factory.CreateMatchFilter();
    filter.Field = PackageMatchField.CatalogDefault;
    filter.Option = PackageFieldMatchOption.ContainsCaseInsensitive;
    filter.Value = string.Join(' ', args.Skip(1));

    var options = factory.CreateFindPackagesOptions();
    options.Selectors.Add(filter);
    options.ResultLimit = ResultLimit;

    var result = await connect.PackageCatalog.FindPackagesAsync(options);
    if (result.Status != FindPackagesResultStatus.Ok)
    {
        Console.Error.WriteLine($"Arama başarısız: {result.Status}");
        return 1;
    }

    foreach (var match in result.Matches.Take(ResultLimit))
    {
        var package = match.CatalogPackage;
        Console.WriteLine($"{package.Id} | {package.Name} | {package.DefaultInstallVersion?.Version}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
