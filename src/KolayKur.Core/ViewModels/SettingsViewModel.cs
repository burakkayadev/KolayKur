using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Support;

namespace KolayKur.Core.ViewModels;

/// <summary>
/// Ayarlar sayfası: dil seçimi (hemen uygulanır ve <c>settings.json</c>'a kaydedilir), winget durumu, onarım ve günlük.
/// Kabuk ve bu ViewModel uygulama boyunca yaşar; kabuğun olayına abonelik bu yüzden bırakılmaz.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string HealthyKey = "Settings_WingetHealthy";
    public const string BrokenKey = "Settings_WingetBroken";
    public const string UnknownKey = "Settings_WingetUnknown";

    private readonly ShellViewModel shell;
    private readonly FileLog log;
    private readonly IDialogService dialogs;
    private readonly string settingsPath;

    public SettingsViewModel(ShellViewModel shell, FileLog log, IDialogService dialogs, string settingsPath)
    {
        this.shell = shell;
        this.log = log;
        this.dialogs = dialogs;
        this.settingsPath = settingsPath;
        shell.PropertyChanged += OnShellPropertyChanged;
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(Language)));
    }

    /// <summary>
    /// Arayüz dili kodu (<c>tr</c>, <c>en</c>). Atamak dili hemen uygular ve kaydeder; desteklenmeyen kod Türkçeye düşer.
    /// </summary>
    public string Language
    {
        get => Loc.Instance.Language;
        set
        {
            var before = Loc.Instance.Language;
            Loc.Instance.SetLanguage(value);
            var applied = Loc.Instance.Language;
            if (!string.Equals(before, applied, StringComparison.Ordinal))
            {
                SaveLanguage(applied);
            }
        }
    }

    public WingetDiagnosis? Diagnosis => shell.Diagnosis;

    /// <summary>Winget durumunu anlatan metin anahtarı.</summary>
    public string StatusKey => Diagnosis switch
    {
        null => UnknownKey,
        { State: WingetState.Healthy } => HealthyKey,
        _ => BrokenKey,
    };

    /// <summary>Çalışan winget'in sürümü; çalışmıyorsa boş.</summary>
    public string WingetVersion => shell.WingetVersion;

    /// <summary>Winget sağlıklıysa onarım gerekmediğini söyler; değilse onarım sayfasını açar.</summary>
    [RelayCommand]
    private void OpenRepair()
    {
        if (Diagnosis?.State == WingetState.Healthy)
        {
            dialogs.Inform(Loc.Instance["Settings_RepairNotNeeded"]);
            return;
        }

        shell.CurrentPage = ShellPage.Repair;
    }

    /// <summary>Günlük dosyasını açar. Henüz yazılmamışsa açılacak bir şey olsun diye önce bir satır yazılır.</summary>
    [RelayCommand]
    private void OpenLog()
    {
        log.TryInfo("Günlük dosyası açılıyor.");
        try
        {
            dialogs.OpenFile(log.CurrentFile);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            log.TryError($"Günlük dosyası açılamadı: {log.CurrentFile}", ex);
            dialogs.Inform(Loc.Instance["Settings_LogOpenFailed"]);
        }
    }

    // Ayarlar dosyasının diğer değerleri (örn. lisans notu) korunur. Kaydedilemezse dil yine de değişmiştir;
    // bu açılışı bozmaz, hata günlüğe yazılır.
    private void SaveLanguage(string code)
    {
        try
        {
            (AppSettings.Load(settingsPath) with { Language = code }).Save(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.TryError("Dil ayarı kaydedilemedi.", ex);
        }
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.Diagnosis))
        {
            return;
        }

        OnPropertyChanged(nameof(Diagnosis));
        OnPropertyChanged(nameof(StatusKey));
        OnPropertyChanged(nameof(WingetVersion));
    }
}
