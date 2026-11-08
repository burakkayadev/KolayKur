using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.ViewModels;

/// <summary>Arama sonuçlarında onay kutulu bir paket satırı.</summary>
public sealed partial class SelectablePackage(PackageInfo package) : ObservableObject
{
    public PackageInfo Package { get; } = package;

    /// <summary>Kartta gösterilen sürüm: önce kullanılabilir, yoksa kurulu sürüm.</summary>
    public string? DisplayVersion => Package.AvailableVersion ?? Package.InstalledVersion;

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>
/// Ara sayfası. Komutlar arayüz iş parçacığında çalışır: sonuçlar <c>await</c> sonrasında aynı bağlamda yazılır.
/// Yeni arama eskisini iptal eder; iptale saygı göstermeyen bir istemcide bile eski sonuç yenisinin üstüne yazılmaz.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    public const string NoInternetKey = "Error_NoInternet";
    public const string GenericErrorKey = "Error_Generic";

    // Yalnızca arayüz iş parçacığında okunup yazılır. Eski kaynaklar bilerek Dispose edilmez: eski arama hâlâ
    // belirtecini kullanıyor olabilir ve zamanlayıcı/bekleme tutamacı olmayan bir kaynak çöp toplayıcıya bırakılır.
    private CancellationTokenSource? currentSearch;
    private string? lastQuery;

    // Son başarılı aramanın sorgusu ve sonuç sayısı; null: henüz sonuç yok (ya da arama başarısız).
    private string? resultQuery;
    private int resultCount;

    private readonly IWingetClient client;
    private readonly OperationQueue queue;
    private readonly FileLog? log;

    public SearchViewModel(IWingetClient client, OperationQueue queue, FileLog? log = null)
    {
        this.client = client;
        this.queue = queue;
        this.log = log;
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(ResultCountText)));
    }

    [ObservableProperty]
    private string query = "";

    /// <summary>Başarısız aramanın hata metni anahtarı; yeni aramada ve başarıda <c>null</c>.</summary>
    [ObservableProperty]
    private string? errorKey;

    /// <summary>Son başlatılan arama sürüyor.</summary>
    [ObservableProperty]
    private bool isLoading;

    public ObservableCollection<SelectablePackage> Results { get; } = [];

    /// <summary>İşaretli sonuç sayısı; her sonucun <c>IsSelected</c> değerini izler.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private int selectedCount;

    /// <summary>"“git” için 5 sonuç"; henüz başarılı bir arama yoksa <c>null</c>. Dil değişince yenilenir.</summary>
    public string? ResultCountText =>
        resultQuery is null ? null : string.Format(Loc.Instance["Search_ResultCount"], resultQuery, resultCount);

    /// <summary>Boş veya yalnızca boşluk içeren sorguda winget'e gitmez.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync() => RunSearchAsync(Query.Trim());

    /// <summary>Hata sonrası, kutudaki metne değil son aranan metne göre aramayı yeniden çalıştırır.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RetryAsync() => RunSearchAsync(lastQuery);

    [RelayCommand]
    private void Install(PackageInfo? package)
    {
        if (package is not null)
        {
            queue.Enqueue(OperationKind.Install, package);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void InstallSelected()
    {
        foreach (var row in Results.Where(row => row.IsSelected))
        {
            queue.Enqueue(OperationKind.Install, row.Package);
        }
    }

    private bool HasSelection() => SelectedCount > 0;

    private async Task RunSearchAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        lastQuery = text;
        var search = new CancellationTokenSource();
        currentSearch?.Cancel();
        currentSearch = search;
        ErrorKey = null;
        IsLoading = true;

        try
        {
            var found = await client.SearchAsync(text, search.Token);
            if (IsCurrent(search))
            {
                ReplaceResults(found);
                resultQuery = text;
                resultCount = found.Count;
                OnPropertyChanged(nameof(ResultCountText));
            }
        }
        catch (OperationCanceledException) when (search.IsCancellationRequested)
        {
            // Yeni arama bu aramayı iptal etti; sonucu zaten yenisi gösteriyor.
        }
        catch (CatalogUnavailableException)
        {
            Fail(search, NoInternetKey);
        }
        catch (Exception ex)
        {
            // Yutulmaz: ayrıntı günlüğe ve izleme çıktısına gider, kullanıcıya genel hata ve "Tekrar dene" gösterilir.
            log.ReportUnexpected("Arama başarısız.", ex);
            Fail(search, GenericErrorKey);
        }
        finally
        {
            if (IsCurrent(search))
            {
                IsLoading = false;
            }
        }
    }

    private bool IsCurrent(CancellationTokenSource search) => ReferenceEquals(currentSearch, search);

    private void Fail(CancellationTokenSource search, string key)
    {
        if (IsCurrent(search))
        {
            ReplaceResults([]);
            resultQuery = null;
            OnPropertyChanged(nameof(ResultCountText));
            ErrorKey = key;
        }
    }

    private void ReplaceResults(IReadOnlyList<PackageInfo> found)
    {
        // Eski satırların aboneliği bırakılır: artık sayıyı etkilememeli ve bellekte tutulmamalı.
        foreach (var row in Results)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Results.Clear();
        foreach (var package in found)
        {
            var row = new SelectablePackage(package);
            row.PropertyChanged += OnRowChanged;
            Results.Add(row);
        }

        UpdateSelectedCount();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectablePackage.IsSelected))
        {
            UpdateSelectedCount();
        }
    }

    private void UpdateSelectedCount() => SelectedCount = Results.Count(row => row.IsSelected);
}
