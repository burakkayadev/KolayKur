using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;
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
    private readonly FileLog? log;

    // Yalnızca arayüz iş parçacığında okunup yazılır; eski kaynaklar bilerek Dispose edilmez (bkz. SearchViewModel).
    private CancellationTokenSource? currentLoad;

    [ObservableProperty]
    private string? errorKey;

    /// <summary>Son başlatılan yükleme sürüyor.</summary>
    [ObservableProperty]
    private bool isLoading;

    public UpdatesViewModel(IWingetClient client, OperationQueue queue, FileLog? log = null)
    {
        this.client = client;
        this.queue = queue;
        this.log = log;
        queue.BatchCompleted += OnBatchCompleted;
        Updates.CollectionChanged += (_, _) => OnCountChanged();
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(CountText)));
    }

    public ObservableCollection<PackageInfo> Updates { get; } = [];

    /// <summary>Listedeki güncelleme sayısı; başarısız yüklemede 0.</summary>
    public int UpdateCount => Updates.Count;

    public bool HasUpdates => UpdateCount > 0;

    /// <summary>"3 güncelleme var"; güncelleme yoksa <c>null</c>. Dil değişince yenilenir.</summary>
    public string? CountText => HasUpdates ? string.Format(Loc.Instance["Updates_Count"], UpdateCount) : null;

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
            // Yutulmaz: ayrıntı günlüğe ve izleme çıktısına gider, kullanıcıya genel hata ve "Tekrar dene" gösterilir.
            log.ReportUnexpected("Güncelleme listesi yüklenemedi.", ex);
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

    private void OnCountChanged()
    {
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(CountText));
    }

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
