using System.Collections.ObjectModel;
using System.Diagnostics;
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
/// Yüklü uygulamalar sayfası: liste, kaldırma (önce onay), paket listesini dosyaya aktarma ve dosyadan kurma.
/// Her toplu iş bitince liste yeniden yüklenir (<see cref="OperationQueue.BatchCompleted"/> arayüz bağlamında gelir).
/// Yüklemeler üst üste binerse en son başlatılan kazanır. <paramref name="wingetVersion"/> dışa aktarılan dosyaya yazılır.
/// </summary>
public sealed partial class InstalledViewModel : ObservableObject
{
    public const string ListFileErrorKey = "Error_ListFile";
    public const string ListSaveErrorKey = "Error_ListSave";

    private readonly IWingetClient client;
    private readonly OperationQueue queue;
    private readonly IDialogService dialogs;
    private readonly Func<string> wingetVersion;

    // Yalnızca arayüz iş parçacığında okunup yazılır; eski kaynaklar bilerek Dispose edilmez (bkz. SearchViewModel).
    private CancellationTokenSource? currentLoad;

    [ObservableProperty]
    private string? errorKey;

    /// <summary>Son başlatılan yükleme sürüyor.</summary>
    [ObservableProperty]
    private bool isLoading;

    public InstalledViewModel(
        IWingetClient client, OperationQueue queue, IDialogService dialogs, Func<string> wingetVersion)
    {
        this.client = client;
        this.queue = queue;
        this.dialogs = dialogs;
        this.wingetVersion = wingetVersion;
        queue.BatchCompleted += OnBatchCompleted;
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
            // Yutulmaz: ayrıntı izleme çıktısına gider, kullanıcıya genel hata ve "Tekrar dene" gösterilir.
            Trace.TraceError($"KolayKur yüklü uygulamalar listesi yüklenemedi: {ex}");
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

    /// <summary>Yüklü uygulamaların kimliklerini seçilen dosyaya yazar; seçici iptal edilirse bir şey yapmaz.</summary>
    [RelayCommand]
    private void Export()
    {
        var path = dialogs.PickSaveFile();
        if (path is null)
        {
            return;
        }

        ErrorKey = null;
        try
        {
            PackageListFile.Save(path, Packages.ToList(), wingetVersion());
        }
        catch (Exception ex)
        {
            Trace.TraceError($"KolayKur paket listesi yazılamadı ({path}): {ex}");
            ErrorKey = ListSaveErrorKey;
        }
    }

    /// <summary>
    /// Seçilen dosyadaki her kimlik için kurulum işi ekler. Dosya okunamazsa hiçbir iş eklenmez; katalogda olmayan
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
        catch (Exception ex)
        {
            Trace.TraceError($"KolayKur paket listesi okunamadı ({path}): {ex}");
            ErrorKey = ListFileErrorKey;
            return;
        }

        foreach (var id in identifiers)
        {
            queue.Enqueue(OperationKind.Install, new PackageInfo(id, id, string.Empty, null, null));
        }
    }

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
