using System.ComponentModel;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Queue;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;
using KolayKur.Core.ViewModels;

namespace KolayKur.Core.Tests.ViewModels;

// Dil değişimi Loc.Instance üzerinde olduğu için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    private readonly FakeDialogService dialogs = new();

    public void Dispose()
    {
        Loc.Instance.SetLanguage("tr");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(directory, "settings.json");

    private FileLog Log => new(Path.Combine(directory, "logs"));

    private async Task<(SettingsViewModel ViewModel, ShellViewModel Shell)> CreateAsync(
        ISystemProbe? probe = null, IThemeService? themes = null, AppTheme theme = AppTheme.System)
    {
        var shell = new ShellViewModel(
            new WingetHealthService(probe ?? new FakeSystemProbe()),
            new OperationQueue(new FakeWingetClient(), Log),
            dialogs,
            SettingsPath,
            log: Log);
        // Lisans notu bir kez gösterilmiş olsun; Inform çağrıları yalnızca ayarlar sayfasından gelsin.
        Directory.CreateDirectory(directory);
        new AppSettings(Language: "tr", LicenseNoticeShown: true, LicenseNoticeVersion: 2, Theme: theme).Save(SettingsPath);
        await shell.StartAsync(CancellationToken.None);
        return (new SettingsViewModel(shell, Log, dialogs, SettingsPath, themes), shell);
    }

    // ---- Dil ----

    [Fact]
    public async Task Language_change_is_saved_and_applied()
    {
        // Arrange
        var (viewModel, _) = await CreateAsync();

        // Act
        viewModel.Language = "en";

        // Assert
        Assert.Equal("en", Loc.Instance.Language);
        Assert.Equal("Search", Loc.Instance["Menu_Search"]);
        Assert.Equal("en", AppSettings.Load(SettingsPath).Language);
    }

    [Fact]
    public async Task Language_change_keeps_other_settings()
    {
        var (viewModel, _) = await CreateAsync();

        viewModel.Language = "en";

        Assert.True(AppSettings.Load(SettingsPath).LicenseNoticeShown);
    }

    [Fact]
    public async Task Language_starts_with_the_applied_language()
    {
        Loc.Instance.SetLanguage("en");

        var (viewModel, _) = await CreateAsync();

        // Kabuk açılışta ayardaki dili (tr) uygular.
        Assert.Equal("tr", viewModel.Language);
    }

    [Fact]
    public async Task Language_change_raises_property_changed_and_ignores_same_value()
    {
        var (viewModel, _) = await CreateAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.Language = "tr";
        Assert.Empty(changed);

        viewModel.Language = "en";
        Assert.Equal([nameof(SettingsViewModel.Language)], changed);
    }

    [Fact]
    public async Task Unsupported_language_falls_back_without_breaking_the_setting()
    {
        var (viewModel, _) = await CreateAsync();
        viewModel.Language = "en";

        viewModel.Language = "xx";

        Assert.Equal("tr", viewModel.Language);
        Assert.Equal("tr", Loc.Instance.Language);
        Assert.Equal("tr", AppSettings.Load(SettingsPath).Language);
    }

    [Fact]
    public async Task Language_is_still_applied_when_saving_fails()
    {
        // Arrange: ayar dosyasının yerinde bir klasör var, dosya yazılamaz.
        var (viewModel, _) = await CreateAsync();
        File.Delete(SettingsPath);
        Directory.CreateDirectory(SettingsPath);

        // Act
        viewModel.Language = "en";

        // Assert: arayüz dili yine değişir, hata günlüğe yazılır, program çökmez.
        Assert.Equal("en", Loc.Instance.Language);
        Assert.Contains("Dil ayarı kaydedilemedi", File.ReadAllText(Log.CurrentFile), StringComparison.Ordinal);
    }

    // ---- Tema ----

    [Fact]
    public async Task Theme_starts_from_saved_setting()
    {
        var (viewModel, _) = await CreateAsync(theme: AppTheme.Dark);

        Assert.Equal(AppTheme.Dark, viewModel.Theme);
    }

    [Fact]
    public async Task Theme_change_is_applied_and_saved()
    {
        // Arrange
        var themes = new FakeThemeService();
        var (viewModel, _) = await CreateAsync(themes: themes);
        viewModel.Language = "en";

        // Act
        viewModel.Theme = AppTheme.Dark;

        // Assert
        Assert.Equal([AppTheme.Dark], themes.Applied);
        var saved = AppSettings.Load(SettingsPath);
        Assert.Equal(AppTheme.Dark, saved.Theme);
        Assert.Equal("en", saved.Language);
        Assert.True(saved.LicenseNoticeShown);
    }

    [Fact]
    public async Task Theme_change_raises_property_changed()
    {
        var (viewModel, _) = await CreateAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.Theme = AppTheme.Light;

        Assert.Equal([nameof(SettingsViewModel.Theme)], changed);
    }

    [Fact]
    public async Task Theme_save_failure_still_applies()
    {
        // Arrange: ayar dosyasının yerinde bir klasör var, dosya yazılamaz.
        var themes = new FakeThemeService();
        var (viewModel, _) = await CreateAsync(themes: themes);
        File.Delete(SettingsPath);
        Directory.CreateDirectory(SettingsPath);

        // Act
        viewModel.Theme = AppTheme.Dark;

        // Assert
        Assert.Equal([AppTheme.Dark], themes.Applied);
        Assert.Equal(AppTheme.Dark, viewModel.Theme);
        Assert.Contains("Tema ayarı kaydedilemedi", File.ReadAllText(Log.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setting_the_same_theme_does_nothing()
    {
        // Arrange
        var themes = new FakeThemeService();
        var (viewModel, _) = await CreateAsync(themes: themes);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        File.Delete(SettingsPath);

        // Act
        viewModel.Theme = AppTheme.System;

        // Assert
        Assert.Empty(themes.Applied);
        Assert.Empty(changed);
        Assert.False(File.Exists(SettingsPath));
    }

    // ---- Winget durumu ----

    [Fact]
    public async Task Status_shows_healthy_winget_with_version()
    {
        var (viewModel, _) = await CreateAsync();

        Assert.Equal(WingetState.Healthy, viewModel.Diagnosis?.State);
        Assert.Equal("Settings_WingetHealthy", viewModel.StatusKey);
        Assert.Equal("1.29.380", viewModel.WingetVersion);
    }

    [Fact]
    public async Task Status_shows_broken_winget_without_version()
    {
        var probe = new FakeSystemProbe
        {
            WingetResult = new ProbeResult(false, "", "winget yok"),
            AppInstallerVersion = null,
        };

        var (viewModel, _) = await CreateAsync(probe);

        Assert.Equal("Settings_WingetBroken", viewModel.StatusKey);
        Assert.Equal(string.Empty, viewModel.WingetVersion);
    }

    [Fact]
    public async Task Status_follows_the_shell_diagnosis()
    {
        // Arrange: kabuk henüz denetlememiş.
        var shell = new ShellViewModel(
            new WingetHealthService(new FakeSystemProbe()), new OperationQueue(new FakeWingetClient(), Log),
            dialogs, SettingsPath, log: Log);
        var viewModel = new SettingsViewModel(shell, Log, dialogs, SettingsPath);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Equal("Settings_WingetUnknown", viewModel.StatusKey);

        // Act
        Directory.CreateDirectory(directory);
        new AppSettings(Language: "tr", LicenseNoticeShown: true, LicenseNoticeVersion: 2).Save(SettingsPath);
        await shell.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Settings_WingetHealthy", viewModel.StatusKey);
        Assert.Contains(nameof(SettingsViewModel.StatusKey), changed);
        Assert.Contains(nameof(SettingsViewModel.WingetVersion), changed);
        Assert.Contains(nameof(SettingsViewModel.Diagnosis), changed);
    }

    // ---- Onar ----

    private (ShellViewModel Shell, SettingsViewModel ViewModel) CreateUnstarted(ISystemProbe probe)
    {
        var repair = new WingetRepairService(new FakeRepairActions(), new WingetHealthService(probe), Log);
        var shell = new ShellViewModel(
            new WingetHealthService(probe),
            new OperationQueue(new FakeWingetClient(), Log),
            dialogs,
            SettingsPath,
            d => new RepairViewModel(repair, d, Log),
            Log);
        Directory.CreateDirectory(directory);
        new AppSettings(Language: "tr", LicenseNoticeShown: true, LicenseNoticeVersion: 2).Save(SettingsPath);
        return (shell, new SettingsViewModel(shell, Log, dialogs, SettingsPath));
    }

    [Fact]
    public async Task Open_repair_when_winget_is_healthy_only_informs()
    {
        // Arrange
        var (viewModel, shell) = await CreateAsync();
        shell.CurrentPage = ShellPage.Settings;

        // Act
        await viewModel.OpenRepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal([Loc.Instance["Settings_RepairNotNeeded"]], dialogs.Informed);
        Assert.Equal(ShellPage.Settings, shell.CurrentPage);
    }

    [Fact]
    public async Task Open_repair_when_winget_is_broken_shows_repair_page()
    {
        // Arrange
        var probe = new ControllableSystemProbe();
        var (shell, viewModel) = CreateUnstarted(probe);
        await shell.StartAsync(CancellationToken.None);
        shell.CurrentPage = ShellPage.Settings;

        // Act
        await viewModel.OpenRepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(ShellPage.Repair, shell.CurrentPage);
        Assert.NotNull(shell.Repair);
        Assert.Empty(dialogs.Informed);
    }

    [Fact]
    public async Task Open_repair_runs_the_check_again_and_opens_repair_when_winget_broke_after_startup()
    {
        // Arrange: açılışta sağlıklı, sonra bozuldu.
        var probe = new ControllableSystemProbe { Working = true };
        var (shell, viewModel) = CreateUnstarted(probe);
        await shell.StartAsync(CancellationToken.None);
        shell.CurrentPage = ShellPage.Settings;
        Assert.Equal(WingetState.Healthy, shell.Diagnosis?.State);
        probe.Working = false;

        // Act
        await viewModel.OpenRepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(WingetState.NotInstalled, shell.Diagnosis?.State);
        Assert.Equal(ShellPage.Repair, shell.CurrentPage);
        Assert.NotNull(shell.Repair);
        Assert.Empty(dialogs.Informed);
    }

    [Fact]
    public async Task Open_repair_informs_when_the_new_check_finds_winget_working_again()
    {
        // Arrange: açılışta bozuktu (onarım sayfası), kullanıcı ayarlara geçti, bu arada düzeldi.
        var probe = new ControllableSystemProbe();
        var (shell, viewModel) = CreateUnstarted(probe);
        await shell.StartAsync(CancellationToken.None);
        shell.CurrentPage = ShellPage.Settings;
        probe.Working = true;

        // Act
        await viewModel.OpenRepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(WingetState.Healthy, shell.Diagnosis?.State);
        Assert.Equal([Loc.Instance["Settings_RepairNotNeeded"]], dialogs.Informed);
        Assert.Equal(ShellPage.Settings, shell.CurrentPage);
    }

    [Fact]
    public async Task Open_repair_works_after_the_startup_check_threw()
    {
        // Arrange: bozuk AppX durumunda açılış denetimi istisna fırlatır; tanı yok.
        var probe = new ControllableSystemProbe { Failure = new InvalidOperationException("AppX bozuk") };
        var (shell, viewModel) = CreateUnstarted(probe);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.StartAsync(CancellationToken.None));
        Assert.Null(shell.Diagnosis);
        shell.CurrentPage = ShellPage.Settings;
        probe.Failure = null;

        // Act
        await viewModel.OpenRepairCommand.ExecuteAsync(null);

        // Assert: boş sayfa değil, gerçek tanıyla onarım sayfası.
        Assert.Equal(WingetState.NotInstalled, shell.Diagnosis?.State);
        Assert.Equal(ShellPage.Repair, shell.CurrentPage);
        Assert.NotNull(shell.Repair);
    }

    [Fact]
    public async Task Open_repair_shows_an_error_and_stays_on_settings_when_the_check_throws()
    {
        // Arrange
        var probe = new ControllableSystemProbe { Working = true };
        var (shell, viewModel) = CreateUnstarted(probe);
        await shell.StartAsync(CancellationToken.None);
        shell.CurrentPage = ShellPage.Settings;
        probe.Failure = new InvalidOperationException("AppX bozuk");

        // Act
        await viewModel.OpenRepairCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal([Loc.Instance["Settings_RepairCheckFailed"]], dialogs.Informed);
        Assert.Equal(ShellPage.Settings, shell.CurrentPage);
        Assert.False(shell.IsChecking);
        Assert.Contains("AppX bozuk", File.ReadAllText(Log.CurrentFile), StringComparison.Ordinal);
    }

    // ---- Günlük ----

    [Fact]
    public async Task Open_log_opens_current_file()
    {
        // Arrange
        var (viewModel, _) = await CreateAsync();

        // Act
        viewModel.OpenLogCommand.Execute(null);

        // Assert
        Assert.Equal([Log.CurrentFile], dialogs.Opened);
    }

    [Fact]
    public async Task Open_log_creates_the_file_so_there_is_something_to_open()
    {
        var (viewModel, _) = await CreateAsync();
        Assert.False(File.Exists(Log.CurrentFile));

        viewModel.OpenLogCommand.Execute(null);

        Assert.True(File.Exists(Log.CurrentFile));
    }

    [Fact]
    public async Task Open_log_failure_informs_the_user_instead_of_crashing()
    {
        // Arrange
        var (viewModel, _) = await CreateAsync();
        dialogs.OpenFileException = new Win32Exception("ilişkili uygulama yok");

        // Act
        viewModel.OpenLogCommand.Execute(null);

        // Assert
        Assert.Equal([Loc.Instance["Settings_LogOpenFailed"]], dialogs.Informed);
        Assert.Contains("ilişkili uygulama yok", File.ReadAllText(Log.CurrentFile), StringComparison.Ordinal);
    }
}
