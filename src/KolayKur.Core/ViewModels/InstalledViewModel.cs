using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Lists;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.ViewModels;

/// <summary>
/// Yüklü uygulamalar sayfası: liste, kaldırma (önce onay), paket listesini dosyaya aktarma ve dosyadan kurma (önce onay).
/// Her toplu iş bitince liste yeniden yüklenir (<see cref="OperationQueue.BatchCompleted"/> arayüz bağlamında gelir).
/// Yüklemeler üst üste binerse en son başlatılan kazanır. <paramref name="wingetVersion"/> dışa aktarılan dosyaya yazılır;
/// <paramref name="log"/> beklenmedik hataları günlüğe yazar (verilmezse yalnızca izleme çıktısına gider).
/// </summary>
public sealed partial class InstalledViewModel : ObservableObject
{
    public const string ListFileErrorKey = "Error_ListFile";
    public const string ListTooLargeErrorKey = "Error_ListTooLarge";
    public const string ListSaveErrorKey = "Error_ListSave";

    // "Listeden kur" onayında adıyla gösterilen uygulama sayısı; kalanı "+N daha" olarak sayılır.
    private const int ConfirmPreviewCount = 5;

    private readonly IWingetClient client;
    private readonly OperationQueue queue;
    private readonly IDialogService dialogs;
    private readonly Func<string> wingetVersion;
    private readonly FileLog? log;

    // Yalnızca arayüz iş parçacığında okunup yazılır; eski kaynaklar bilerek Dispose edilmez (bkz. SearchViewModel).
    private CancellationTokenSource? currentLoad;

    [ObservableProperty]
    private string? errorKey;

    /// <summary>Son başlatılan yükleme sürüyor.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool isLoading;

    public InstalledViewModel(
        IWingetClient client,
        OperationQueue queue,
        IDialogService dialogs,
        Func<string> wingetVersion,
        FileLog? log = null)
    {
        this.client = client;
        this.queue = queue;
        this.dialogs = dialogs;
        this.wingetVersion = wingetVersion;
        this.log = log;
        queue.BatchCompleted += OnBatchCompleted;
        Packages.CollectionChanged += OnPackagesChanged;
    }

    public ObservableCollection<PackageInfo> Packages { get; } = [];

    /// <summary>Listeyi yükler; hata sonrası "Tekrar dene" de bu komuttur.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync()
    {
        var load = new CancellationTokenSource();
        currentLoad?.Cancel();
        currentLoad = load;
        ErrorKey = null;
        IsLoading = true;

        try
        {
            var found = await client.GetInstalledAsync(load.Token);
            if (IsCurrent(load))
            {
                Packages.Clear();
                foreach (var package in found)
                {
                    Packages.Add(package);
                }
            }
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // Daha yeni bir yükleme bunu iptal etti; sonucu o gösterecek.
        }
        catch (CatalogUnavailableException)
        {
            Fail(load, SearchViewModel.NoInternetKey);
        }
        catch (Exception ex)
        {
            // Yutulmaz: ayrıntı günlüğe ve izleme çıktısına gider, kullanıcıya genel hata ve "Tekrar dene" gösterilir.
            log.ReportUnexpected("Yüklü uygulamalar listesi yüklenemedi.", ex);
            Fail(load, SearchViewModel.GenericErrorKey);
        }
        finally
        {
            if (IsCurrent(load))
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Onay sorar; yalnızca "evet" ise kaldırma işini kuyruğa ekler.</summary>
    [RelayCommand]
    private void Uninstall(PackageInfo? package)
    {
        if (package is null)
        {
            return;
        }

        var question = string.Format(CultureInfo.InvariantCulture, Loc.Instance["Installed_ConfirmUninstall"], package.Name);
        if (dialogs.Confirm(question))
        {
            queue.Enqueue(OperationKind.Uninstall, package);
        }
    }

    /// <summary>
    /// Yüklü uygulamaların kimliklerini seçilen dosyaya yazar; winget kaynağı olmayan kayıtlar (ARP\, MSIX\) yazılmaz,
    /// böylece dosya "Listeden kur" ile geri okununca bulunamayan işler üretmez. Liste yüklenirken veya yazılacak kayıt
    /// yokken kullanılamaz (boş ya da eksik bir dosya yazılmasın). Seçici iptal edilirse bir şey yapmaz; başarıdan sonra
    /// kaç uygulamanın nereye kaydedildiği söylenir.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        // ICommand.Execute CanExecute'u denetlemez; düğme dışından (klavye, kod) çağrılsa da boş/eksik dosya yazılmasın.
        if (!CanExport())
        {
            return;
        }

        var path = dialogs.PickSaveFile();
        if (path is null)
        {
            return;
        }

        ErrorKey = null;
        var exportable = Packages.Where(package => PackageListFile.IsExportable(package.Id)).ToList();
        try
        {
            PackageListFile.Save(path, exportable, wingetVersion());
        }
        catch (Exception ex)
        {
            log.ReportUnexpected($"Paket listesi yazılamadı ({path}).", ex);
            ErrorKey = ListSaveErrorKey;
            return;
        }

        dialogs.Inform(string.Format(CultureInfo.InvariantCulture, Loc.Instance["Installed_ExportDone"], exportable.Count, path));
    }

    private bool CanExport() =>
        !IsLoading && Packages.Any(package => PackageListFile.IsExportable(package.Id));

    /// <summary>
    /// Seçilen dosyadaki her kimlik için kurulum işi ekler, ama önce kaç uygulamanın kurulacağını (ilk adlarıyla) sorar;
    /// "hayır" denirse hiçbir şey başlamaz. Dosya okunamaz ya da çok büyükse hiçbir iş eklenmez; katalogda olmayan
    /// paketler kuyrukta "bulunamadı" olur, diğerleri kurulur.
    /// </summary>
    [RelayCommand]
    private void Import()
    {
        var path = dialogs.PickOpenFile();
        if (path is null)
        {
            return;
        }

        ErrorKey = null;
        IReadOnlyList<string> identifiers;
        try
        {
            identifiers = PackageListFile.Load(path);
        }
        catch (PackageListTooLargeException ex)
        {
            log.ReportUnexpected($"Paket listesi çok büyük ({path}).", ex);
            ErrorKey = ListTooLargeErrorKey;
            return;
        }
        catch (Exception ex)
        {
            log.ReportUnexpected($"Paket listesi okunamadı ({path}).", ex);
            ErrorKey = ListFileErrorKey;
            return;
        }

        if (identifiers.Count == 0)
        {
            dialogs.Inform(Loc.Instance["Installed_ImportEmpty"]);
            return;
        }

        if (!dialogs.Confirm(ConfirmImportText(identifiers)))
        {
            return;
        }

        foreach (var id in identifiers)
        {
            queue.Enqueue(OperationKind.Install, new PackageInfo(id, id, string.Empty, null, null));
        }
    }

    private static string ConfirmImportText(IReadOnlyList<string> identifiers)
    {
        var names = string.Join(", ", identifiers.Take(ConfirmPreviewCount));
        var more = identifiers.Count - ConfirmPreviewCount;
        if (more > 0)
        {
            names += " " + string.Format(CultureInfo.InvariantCulture, Loc.Instance["Installed_ImportMore"], more);
        }

        return string.Format(CultureInfo.InvariantCulture, Loc.Instance["Installed_ConfirmImport"], identifiers.Count, names);
    }

    private void OnPackagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        ExportCommand.NotifyCanExecuteChanged();

    // Olay arayüz bağlamında gelir, yükleme doğrudan başlatılabilir. LoadAsync tüm hataları kendi içinde ele alır.
    private void OnBatchCompleted(object? sender, BatchSummary summary) => _ = LoadCommand.ExecuteAsync(null);

    private bool IsCurrent(CancellationTokenSource load) => ReferenceEquals(currentLoad, load);

    private void Fail(CancellationTokenSource load, string key)
    {
        if (IsCurrent(load))
        {
            Packages.Clear();
            ErrorKey = key;
        }
    }
}
