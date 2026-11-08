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
        private set => SetProperty(ref diagnosis, value);
    }

    /// <summary>Denetim sürerken ve onarım sayfası açıkken menü kullanılamaz.</summary>
    public bool IsMenuEnabled => !IsChecking && CurrentPage != ShellPage.Repair;

    public async Task StartAsync(CancellationToken ct)
    {
        var settings = AppSettings.Load(settingsPath);
        Loc.Instance.SetLanguage(settings.Language);

        await DiagnoseAsync(ct);

        ShowLicenseNoticeOnce(settings);
    }

    /// <summary>İşlem sürerken kapatmak onay ister; boştayken sormadan kapatılır.</summary>
    public bool ConfirmClose() => !queue.IsBusy || dialogs.Confirm(Loc.Instance["Close_Confirm"]);

    // Yoklama eşzamanlı çağrılar yapar; arayüzü dondurmasın diye havuz iş parçacığına alınır. Sayfa değişiklikleri
    // (bağlamaya giden durum) await sonrasında, çağıranın bağlamında yapılır.
    private async Task DiagnoseAsync(CancellationToken ct)
    {
        IsChecking = true;
        try
        {
            var result = await Task.Run(() => health.DiagnoseAsync(ct), ct);
            Diagnosis = result;
            ShowPageFor(result);
        }
        finally
        {
            IsChecking = false;
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
        if (settings.LicenseNoticeShown)
        {
            return;
        }

        dialogs.Inform(Loc.Instance["License_Notice"]);
        try
        {
            (settings with { LicenseNoticeShown = true }).Save(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kaydedilemezse bir sonraki açılışta yeniden gösterilir; bu açılışı engellemez.
            log.TryError("Lisans notu ayarı kaydedilemedi.", ex);
        }
    }
}
