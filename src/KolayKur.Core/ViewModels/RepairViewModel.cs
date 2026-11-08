using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Health;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;

namespace KolayKur.Core.ViewModels;

public sealed partial class RepairViewModel(WingetRepairService repair, WingetDiagnosis diagnosis, FileLog? log = null)
    : ObservableObject
{
    public const string FailedKey = "Repair_Failed";
    public const string VerificationFailedKey = "Repair_VerificationFailed";

    private string? statusText;
    private string? errorKey;

    /// <summary>Onarımın nedenini anlatan metin anahtarı.</summary>
    public string ReasonKey { get; } = diagnosis.State switch
    {
        WingetState.NotInstalled => "Repair_Reason_NotInstalled",
        WingetState.DependencyBroken => "Repair_Reason_DependencyBroken",
        _ => "Repair_Reason_RegistrationBroken",
    };

    /// <summary>Onarım sürerken en son bildirilen adım (hizmetten gelen, kullanıcıya gösterilen metin).</summary>
    public string? StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    /// <summary>Başarısız onarımın hata metni anahtarı; yeni denemede ve başarıda <c>null</c>.</summary>
    public string? ErrorKey
    {
        get => errorKey;
        private set => SetProperty(ref errorKey, value);
    }

    /// <summary>Onarım bitti ve winget yeniden çalışıyor.</summary>
    public event EventHandler? Repaired;

    // Ağır iş (yoklamalar eşzamanlı, indirmeler yüzlerce MB'lık özet hesaplar) Task.Run ile arayüz iş parçacığından
    // çıkarılır; Progress arayüz bağlamında kurulduğu için durum metni arayüz iş parçacığında güncellenir.
    [RelayCommand]
    private async Task RepairAsync()
    {
        ErrorKey = null;
        StatusText = null;
        var progress = new Progress<string>(text => StatusText = text);

        try
        {
            var result = await Task.Run(() => repair.RepairAsync(diagnosis, progress, CancellationToken.None));
            if (result.State != WingetState.Healthy)
            {
                log.TryInfo($"Onarımdan sonra winget hâlâ çalışmıyor: {result.State} {result.Detail}");
                ErrorKey = FailedKey;
                return;
            }
        }
        catch (DownloadVerificationException ex)
        {
            log.TryError("İndirilen dosya doğrulanamadı.", ex);
            ErrorKey = VerificationFailedKey;
            return;
        }
        catch (Exception ex)
        {
            log.TryError("Onarım ekranı: onarım başarısız.", ex);
            ErrorKey = FailedKey;
            return;
        }

        Repaired?.Invoke(this, EventArgs.Empty);
    }
}
