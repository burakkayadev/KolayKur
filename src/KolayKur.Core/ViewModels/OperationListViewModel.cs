using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using KolayKur.Core.Localization;
using KolayKur.Core.Queue;

namespace KolayKur.Core.ViewModels;

/// <summary>
/// İşlem listesi: kuyruktaki işleri sarar ve her toplu iş bitince özet metnini günceller
/// (<see cref="OperationQueue.BatchCompleted"/> arayüz bağlamında gelir). Özet, yeni bir toplu iş başlayınca
/// (kuyruğa iş eklenince) silinir ve dil değişince yeni dilde yeniden yazılır.
/// </summary>
public sealed class OperationListViewModel : ObservableObject
{
    private BatchSummary? lastSummary;

    public OperationListViewModel(OperationQueue queue)
    {
        Items = queue.Items;
        queue.BatchCompleted += (_, summary) => SetLastSummary(summary);
        ((INotifyCollectionChanged)Items).CollectionChanged += OnItemsChanged;
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(SummaryText)));
    }

    public ReadOnlyObservableCollection<OperationItem> Items { get; }

    /// <summary>Son biten toplu işin özeti, geçerli dilde; hiç iş bitmediyse veya yeni toplu iş başladıysa <c>null</c>.</summary>
    public string? SummaryText => lastSummary is null ? null : OperationTexts.SummaryText(lastSummary);

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            SetLastSummary(null);
        }
    }

    private void SetLastSummary(BatchSummary? summary)
    {
        lastSummary = summary;
        OnPropertyChanged(nameof(SummaryText));
    }
}
