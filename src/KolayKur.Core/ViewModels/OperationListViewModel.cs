using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KolayKur.Core.Localization;
using KolayKur.Core.Queue;

namespace KolayKur.Core.ViewModels;

/// <summary>
/// İşlem listesi: kuyruktaki işleri sarar ve her toplu iş bitince özet metnini günceller
/// (<see cref="OperationQueue.BatchCompleted"/> arayüz bağlamında gelir).
/// </summary>
public sealed partial class OperationListViewModel : ObservableObject
{
    public OperationListViewModel(OperationQueue queue)
    {
        Items = queue.Items;
        queue.BatchCompleted += (_, summary) => SummaryText = OperationTexts.SummaryText(summary);
    }

    public ReadOnlyObservableCollection<OperationItem> Items { get; }

    /// <summary>Son biten toplu işin özeti; hiç iş bitmediyse <c>null</c>.</summary>
    [ObservableProperty]
    private string? summaryText;
}
