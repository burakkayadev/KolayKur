using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;

namespace KolayKur.Core.ViewModels;

/// <summary>
/// İşlem listesi ve İşlemler paneli: kuyruktaki işleri sarar, etkin iş sayısını ve panelin açık/kapalı durumunu
/// tutar, her toplu iş bitince özet metnini günceller (<see cref="OperationQueue.BatchCompleted"/> arayüz bağlamında
/// gelir). Özet, yeni bir toplu iş başlayınca (kuyruğa iş eklenince) silinir ve dil değişince yeniden yazılır.
/// Sayaçlar her öğenin <c>Status</c> değişiminde ve koleksiyon değişiminde yeniden hesaplanır; kaldırılan öğenin
/// aboneliği bırakılır. Her şey arayüz iş parçacığında çalışır (kuyruk olaylarını oraya taşır).
/// </summary>
public sealed class OperationListViewModel : ObservableObject
{
    private readonly OperationQueue queue;
    private readonly HashSet<OperationItem> watched = [];
    private BatchSummary? lastSummary;
    private int activeCount;
    private bool hasFinished;
    private bool isPaneOpen;

    public OperationListViewModel(OperationQueue queue)
    {
        this.queue = queue;
        Items = queue.Items;
        TogglePaneCommand = new RelayCommand(() => IsPaneOpen = !IsPaneOpen);
        ClosePaneCommand = new RelayCommand(() => IsPaneOpen = false);
        ClearFinishedCommand = new RelayCommand(() => this.queue.ClearFinished(), () => HasFinished);

        queue.BatchCompleted += (_, summary) => SetLastSummary(summary);
        ((INotifyCollectionChanged)Items).CollectionChanged += OnItemsChanged;
        Watch(Items);
        RecomputeCounts();
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(SummaryText)));
    }

    public ReadOnlyObservableCollection<OperationItem> Items { get; }

    /// <summary>Son biten toplu işin özeti, geçerli dilde; hiç iş bitmediyse veya yeni toplu iş başladıysa <c>null</c>.</summary>
    public string? SummaryText => lastSummary is null ? null : OperationTexts.SummaryText(lastSummary);

    /// <summary>Sıradaki ve çalışan iş sayısı.</summary>
    public int ActiveCount
    {
        get => activeCount;
        private set
        {
            if (SetProperty(ref activeCount, value))
            {
                OnPropertyChanged(nameof(HasActive));
            }
        }
    }

    public bool HasActive => activeCount > 0;

    /// <summary>Listede bitmiş (başarılı, başarısız, iptal, bulunamadı) en az bir iş var mı.</summary>
    public bool HasFinished
    {
        get => hasFinished;
        private set
        {
            if (SetProperty(ref hasFinished, value))
            {
                ClearFinishedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>İşlemler paneli açık mı; kuyruğa iş eklenince kendiliğinden açılır.</summary>
    public bool IsPaneOpen
    {
        get => isPaneOpen;
        set => SetProperty(ref isPaneOpen, value);
    }

    public IRelayCommand TogglePaneCommand { get; }

    public IRelayCommand ClosePaneCommand { get; }

    /// <summary>Bitmiş işleri listeden kaldırır; yalnızca bitmiş iş varken etkindir.</summary>
    public IRelayCommand ClearFinishedCommand { get; }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
        {
            Unwatch(e.OldItems);
            Watch(e.NewItems);
        }
        else
        {
            Unwatch(watched.ToList());
            Watch(Items);
        }

        RecomputeCounts();
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            IsPaneOpen = true;
            SetLastSummary(null);
        }
    }

    private void Watch(IEnumerable? newItems)
    {
        foreach (var item in (newItems ?? Array.Empty<OperationItem>()).OfType<OperationItem>())
        {
            if (watched.Add(item))
            {
                item.PropertyChanged += OnItemChanged;
            }
        }
    }

    /// <summary>Kaldırılan öğelerin aboneliğini bırakır; böylece listeden çıkan öğe ViewModel'i canlı tutmaz.</summary>
    private void Unwatch(IEnumerable? oldItems)
    {
        foreach (var item in (oldItems ?? Array.Empty<OperationItem>()).OfType<OperationItem>())
        {
            if (watched.Remove(item))
            {
                item.PropertyChanged -= OnItemChanged;
            }
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationItem.Status))
        {
            RecomputeCounts();
        }
    }

    private void RecomputeCounts()
    {
        var active = 0;
        var finished = false;
        foreach (var item in Items)
        {
            if (item.Status is OperationStatus.Queued or OperationStatus.Running)
            {
                active++;
            }
            else
            {
                finished = true;
            }
        }

        ActiveCount = active;
        HasFinished = finished;
    }

    private void SetLastSummary(BatchSummary? summary)
    {
        lastSummary = summary;
        OnPropertyChanged(nameof(SummaryText));
    }
}
