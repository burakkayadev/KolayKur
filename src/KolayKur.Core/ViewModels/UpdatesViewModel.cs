using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Winget;

namespace KolayKur.Core.ViewModels;

/// <summary>
/// Güncellemeler sayfası. Her toplu iş bitince liste yeniden yüklenir (<see cref="OperationQueue.BatchCompleted"/>
/// arayüz bağlamında gelir). Yüklemeler üst üste binerse en son başlatılan kazanır.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    private readonly IWingetClient client;
    private readonly OperationQueue queue;

    // Yalnızca arayüz iş parçacığında okunup yazılır; eski kaynaklar bilerek Dispose edilmez (bkz. SearchViewModel).
    private CancellationTokenSource? currentLoad;

    [ObservableProperty]
    private string? errorKey;

    /// <summary>Son başlatılan yükleme sürüyor.</summary>
    [ObservableProperty]
    private bool isLoading;

    public UpdatesViewModel(IWingetClient client, OperationQueue queue)
    {
        this.client = client;
        this.queue = queue;
        queue.BatchCompleted += OnBatchCompleted;
    }

    public ObservableCollection<PackageInfo> Updates { get; } = [];

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
            var found = await client.GetUpgradesAsync(load.Token);
            if (IsCurrent(load))
            {
                Updates.Clear();
                foreach (var package in found)
                {
                    Updates.Add(package);
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
            Trace.TraceError($"KolayKur güncelleme listesi yüklenemedi: {ex}");
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

    [RelayCommand]
    private void Upgrade(PackageInfo? package)
    {
        if (package is not null)
        {
            queue.Enqueue(OperationKind.Upgrade, package);
        }
    }

    [RelayCommand]
    private void UpgradeAll()
    {
        foreach (var package in Updates.ToList())
        {
            queue.Enqueue(OperationKind.Upgrade, package);
        }
    }

    // Olay arayüz bağlamında gelir, yükleme doğrudan başlatılabilir. LoadAsync tüm hataları kendi içinde ele alır.
    private void OnBatchCompleted(object? sender, BatchSummary summary) => _ = LoadCommand.ExecuteAsync(null);

    private bool IsCurrent(CancellationTokenSource load) => ReferenceEquals(currentLoad, load);

    private void Fail(CancellationTokenSource load, string key)
    {
        if (IsCurrent(load))
        {
            Updates.Clear();
            ErrorKey = key;
        }
    }
}
