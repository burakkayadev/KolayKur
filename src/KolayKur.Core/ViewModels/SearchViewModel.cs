using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Winget;

namespace KolayKur.Core.ViewModels;

/// <summary>Arama sonuçlarında onay kutulu bir paket satırı.</summary>
public sealed partial class SelectablePackage(PackageInfo package) : ObservableObject
{
    public PackageInfo Package { get; } = package;

    [ObservableProperty]
    private bool isSelected;
}

/// <summary>
/// Ara sayfası. Komutlar arayüz iş parçacığında çalışır: sonuçlar <c>await</c> sonrasında aynı bağlamda yazılır.
/// Yeni arama eskisini iptal eder; iptale saygı göstermeyen bir istemcide bile eski sonuç yenisinin üstüne yazılmaz.
/// </summary>
public sealed partial class SearchViewModel(IWingetClient client, OperationQueue queue) : ObservableObject
{
    public const string NoInternetKey = "Error_NoInternet";
    public const string GenericErrorKey = "Error_Generic";

    // Yalnızca arayüz iş parçacığında okunup yazılır. Eski kaynaklar bilerek Dispose edilmez: eski arama hâlâ
    // belirtecini kullanıyor olabilir ve zamanlayıcı/bekleme tutamacı olmayan bir kaynak çöp toplayıcıya bırakılır.
    private CancellationTokenSource? currentSearch;
    private string? lastQuery;

    [ObservableProperty]
    private string query = "";

    /// <summary>Başarısız aramanın hata metni anahtarı; yeni aramada ve başarıda <c>null</c>.</summary>
    [ObservableProperty]
    private string? errorKey;

    /// <summary>Son başlatılan arama sürüyor.</summary>
    [ObservableProperty]
    private bool isLoading;

    public ObservableCollection<SelectablePackage> Results { get; } = [];

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

    [RelayCommand]
    private void InstallSelected()
    {
        foreach (var row in Results.Where(row => row.IsSelected))
        {
            queue.Enqueue(OperationKind.Install, row.Package);
        }
    }

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
                Results.Clear();
                foreach (var package in found)
                {
                    Results.Add(new SelectablePackage(package));
                }
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
            // Yutulmaz: ayrıntı izleme çıktısına gider, kullanıcıya genel hata ve "Tekrar dene" gösterilir.
            Trace.TraceError($"KolayKur arama başarısız: {ex}");
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
            Results.Clear();
            ErrorKey = key;
        }
    }
}
