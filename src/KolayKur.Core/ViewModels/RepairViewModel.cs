using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;

namespace KolayKur.Core.ViewModels;

public sealed partial class RepairViewModel : ObservableObject
{
    public const string FailedKey = "Repair_Failed";
    public const string VerificationFailedKey = "Repair_VerificationFailed";
    public const string DownloadStalledKey = "Repair_DownloadStalled";

    private readonly WingetRepairService repair;
    private readonly WingetDiagnosis diagnosis;
    private readonly FileLog? log;

    private RepairStep? latestProgress;
    private string? errorKey;

    public RepairViewModel(WingetRepairService repair, WingetDiagnosis diagnosis, FileLog? log = null)
    {
        this.repair = repair;
        this.diagnosis = diagnosis;
        this.log = log;
        ReasonKey = diagnosis.State switch
        {
            WingetState.NotInstalled => "Repair_Reason_NotInstalled",
            WingetState.DependencyBroken => "Repair_Reason_DependencyBroken",
            _ => "Repair_Reason_RegistrationBroken",
        };

        // Durum metni dilden bağımsız adım olarak saklanır; dil değişince yeniden çevrilir.
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(StatusText)));
    }

    /// <summary>Onarımın nedenini anlatan metin anahtarı.</summary>
    public string ReasonKey { get; }

    /// <summary>
    /// Onarım sürerken en son bildirilen adımın, geçerli dildeki metni; adım yoksa <c>null</c>.
    /// Dil değişince <see cref="INotifyPropertyChanged"/> ile yenilenir.
    /// </summary>
    public string? StatusText => latestProgress is null ? null : FormatStatus(latestProgress);

    /// <summary>Başarısız onarımın hata metni anahtarı; yeni denemede ve başarıda <c>null</c>.</summary>
    public string? ErrorKey
    {
        get => errorKey;
        private set => SetProperty(ref errorKey, value);
    }

    /// <summary>Onarım bitti ve winget yeniden çalışıyor.</summary>
    public event EventHandler? Repaired;

    /// <summary>Onarım adımının metin anahtarı (<c>Strings.resx</c>).</summary>
    public static string StageKey(RepairStage stage) => $"Repair_Stage_{stage}";

    /// <summary>Metindeki <c>{0}</c> konu (dosya veya bileşen adı), <c>{1}</c> yüzdedir; sayılar kültürden bağımsız yazılır.</summary>
    private static string FormatStatus(RepairStep progress) =>
        string.Format(
            CultureInfo.InvariantCulture,
            Loc.Instance[StageKey(progress.Stage)],
            progress.Subject,
            progress.Percent);

    private void SetLatestProgress(RepairStep? progress)
    {
        latestProgress = progress;
        OnPropertyChanged(nameof(StatusText));
    }

    // Ağır iş (yoklamalar eşzamanlı, indirmeler yüzlerce MB'lık özet hesaplar) Task.Run ile arayüz iş parçacığından
    // çıkarılır; Progress arayüz bağlamında kurulduğu için durum metni arayüz iş parçacığında güncellenir.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RepairAsync(CancellationToken cancellationToken)
    {
        ErrorKey = null;
        SetLatestProgress(null);
        var progress = new Progress<RepairStep>(SetLatestProgress);

        try
        {
            var result = await Task.Run(() => repair.RepairAsync(diagnosis, progress, cancellationToken));
            if (result.State != WingetState.Healthy)
            {
                log.TryInfo($"Onarımdan sonra winget hâlâ çalışmıyor: {result.State} {result.Detail}");
                ErrorKey = FailedKey;
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Kullanıcı vazgeçti; hata değildir. Servis iptali zaten günlüğe yazdı.
            return;
        }
        catch (DownloadStalledException ex)
        {
            log.TryError("İndirme takıldı.", ex);
            ErrorKey = DownloadStalledKey;
            return;
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
