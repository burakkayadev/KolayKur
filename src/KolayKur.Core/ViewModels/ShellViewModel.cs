using CommunityToolkit.Mvvm.ComponentModel;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;

namespace KolayKur.Core.ViewModels;

public enum ShellPage { Repair, Search, Updates, Installed, Settings }

/// <summary>
/// Uygulama kabuğu: açılışta ayarları yükler, dili uygular, winget'i denetler ve gösterilecek sayfayı seçer.
/// Winget sağlıklı değilse diğer sayfalar yerine onarım sayfası gelir.
/// <paramref name="createRepair"/> onarım sayfasının ViewModel'ini üretir (verilmezse onarım sayfası boş kalır);
/// <paramref name="log"/> beklenmedik hatalar içindir.
/// </summary>
public sealed partial class ShellViewModel(
    WingetHealthService health,
    OperationQueue queue,
    IDialogService dialogs,
    string settingsPath,
    Func<WingetDiagnosis, RepairViewModel>? createRepair = null,
    FileLog? log = null) : ObservableObject
{
    /// <summary>Lisans notunun güncel sürümü; metin anlamlı biçimde değişince artırılır ve bir kez daha gösterilir.</summary>
    public const int CurrentLicenseNoticeVersion = 2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMenuEnabled))]
    private ShellPage currentPage = ShellPage.Search;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMenuEnabled))]
    private bool isChecking = true;

    [ObservableProperty]
    private RepairViewModel? repair;

    private WingetDiagnosis? diagnosis;

    public WingetDiagnosis? Diagnosis
    {
        get => diagnosis;
        private set => SetDiagnosis(value);
    }

    /// <summary>
    /// Çalışan winget'in sürümü (<c>winget --version</c> çıktısındaki baştaki <c>v</c> atılmış, örn. <c>1.29.380</c>);
    /// winget çalışmıyorsa veya denetim bitmediyse boş metin.
    /// </summary>
    public string WingetVersion => Diagnosis is { State: WingetState.Healthy } healthy ? healthy.Detail.TrimStart('v') : string.Empty;

    /// <summary>
    /// <see cref="Diagnosis"/> önceki değeri <c>null</c> ya da sağlıksızken sağlıklı bir değere geçince, çağıranın
    /// bağlamında bir kez tetiklenir (sağlıklıdan sağlıklıya geçişte tetiklenmez).
    /// </summary>
    public event EventHandler? BecameHealthy;

    /// <summary>Denetim sürerken ve onarım sayfası açıkken menü kullanılamaz.</summary>
    public bool IsMenuEnabled => !IsChecking && CurrentPage != ShellPage.Repair;

    public async Task StartAsync(CancellationToken ct)
    {
        var settings = AppSettings.Load(settingsPath);
        Loc.Instance.SetLanguage(settings.Language);

        // Bildirim winget denetiminden önce gösterilir: Store'a hiçbir bağlantı bildirimden önce kurulmaz.
        ShowLicenseNoticeOnce(settings);

        await DiagnoseAsync(ct);
    }

    /// <summary>
    /// Kuyrukta işlem sürerken veya winget onarımı çalışırken kapatmak onay ister (yarım kalan kurulum bozuk durum
    /// bırakabilir); ikisi de boştayken sormadan kapatılır.
    /// </summary>
    public bool ConfirmClose() =>
        !(queue.IsBusy || Repair?.RepairCommand.IsRunning == true) || dialogs.Confirm(Loc.Instance["Close_Confirm"]);

    // Yoklama eşzamanlı çağrılar yapar; arayüzü dondurmasın diye havuz iş parçacığına alınır. Sayfa değişiklikleri
    // (bağlamaya giden durum) await sonrasında, çağıranın bağlamında yapılır.
    private async Task DiagnoseAsync(CancellationToken ct)
    {
        var result = await RecheckAsync(ct);
        ShowPageFor(result);
    }

    /// <summary>
    /// Winget'i yeniden denetler ve <see cref="Diagnosis"/> değerini günceller; sayfayı değiştirmez. Denetim bir
    /// istisna fırlatırsa (örn. bozuk AppX durumu) istisna çağırana yansır ve <see cref="IsChecking"/> kapanır.
    /// </summary>
    public async Task<WingetDiagnosis> RecheckAsync(CancellationToken ct)
    {
        IsChecking = true;
        try
        {
            var result = await Task.Run(() => health.DiagnoseAsync(ct), ct);
            Diagnosis = result;
            return result;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Onarım sayfasını verilen (sağlıklı olmayan) tanı için açar; sağlıklıysa onarım sayfası kapanır.</summary>
    public void OpenRepairFor(WingetDiagnosis result) => ShowPageFor(result);

    // Onarım sayfası tanısız, sağlıklı bir tanıyla ya da onarım ViewModel'i olmadan boş ve çıkışsız kalır; geri dön.
    partial void OnCurrentPageChanged(ShellPage value)
    {
        if (value == ShellPage.Repair && (Repair is null || Diagnosis is null or { State: WingetState.Healthy }))
        {
            CurrentPage = ShellPage.Search;
        }
    }

    private void SetDiagnosis(WingetDiagnosis? value)
    {
        var wasHealthy = diagnosis is { State: WingetState.Healthy };
        if (SetProperty(ref diagnosis, value, nameof(Diagnosis)))
        {
            OnPropertyChanged(nameof(WingetVersion));
            if (!wasHealthy && value is { State: WingetState.Healthy })
            {
                RaiseBecameHealthy();
            }
        }
    }

    // Durum ayarlandıktan sonra çağrılır; dinleyici hatası tanıyı bozmaz, günlüğe yazılır.
    private void RaiseBecameHealthy()
    {
        try
        {
            BecameHealthy?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            log.TryError("BecameHealthy dinleyicisi hata verdi.", ex);
        }
    }

    private void ShowPageFor(WingetDiagnosis result)
    {
        if (Repair is not null)
        {
            Repair.Repaired -= OnRepaired;
        }

        if (result.State == WingetState.Healthy)
        {
            Repair = null;
            if (CurrentPage == ShellPage.Repair)
            {
                CurrentPage = ShellPage.Search;
            }

            return;
        }

        Repair = createRepair?.Invoke(result);
        if (Repair is not null)
        {
            Repair.Repaired += OnRepaired;
        }

        CurrentPage = ShellPage.Repair;
    }

    private void OnRepaired(object? sender, EventArgs e) => _ = DiagnoseAfterRepairAsync();

    private async Task DiagnoseAfterRepairAsync()
    {
        try
        {
            await DiagnoseAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.TryError("Onarımdan sonra winget denetlenemedi.", ex);
            dialogs.Inform(Loc.Instance["Startup_Failed"]);
        }
    }

    private void ShowLicenseNoticeOnce(AppSettings settings)
    {
        if (settings.EffectiveLicenseNoticeVersion >= CurrentLicenseNoticeVersion)
        {
            return;
        }

        dialogs.Inform(Loc.Instance["License_Notice"]);
        try
        {
            (settings with { LicenseNoticeShown = true, LicenseNoticeVersion = CurrentLicenseNoticeVersion }).Save(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kaydedilemezse bir sonraki açılışta yeniden gösterilir; bu açılışı engellemez.
            log.TryError("Lisans notu ayarı kaydedilemedi.", ex);
        }
    }
}
