using System.ComponentModel;
using KolayKur.Core.Health;
using KolayKur.Core.Localization;
using KolayKur.Core.Queue;
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

    private async Task<(SettingsViewModel ViewModel, ShellViewModel Shell)> CreateAsync(ISystemProbe? probe = null)
    {
        var shell = new ShellViewModel(
            new WingetHealthService(probe ?? new FakeSystemProbe()),
            new OperationQueue(new FakeWingetClient(), Log),
            dialogs,
            SettingsPath,
            log: Log);
        // Lisans notu bir kez gösterilmiş olsun; Inform çağrıları yalnızca ayarlar sayfasından gelsin.
        Directory.CreateDirectory(directory);
        new AppSettings(Language: "tr", LicenseNoticeShown: true).Save(SettingsPath);
        await shell.StartAsync(CancellationToken.None);
        return (new SettingsViewModel(shell, Log, dialogs, SettingsPath), shell);
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
        new AppSettings(Language: "tr", LicenseNoticeShown: true).Save(SettingsPath);
        await shell.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal("Settings_WingetHealthy", viewModel.StatusKey);
        Assert.Contains(nameof(SettingsViewModel.StatusKey), changed);
        Assert.Contains(nameof(SettingsViewModel.WingetVersion), changed);
        Assert.Contains(nameof(SettingsViewModel.Diagnosis), changed);
    }

    // ---- Onar ----

    [Fact]
    public async Task Open_repair_when_winget_is_healthy_only_informs()
    {
        // Arrange
        var (viewModel, shell) = await CreateAsync();
        shell.CurrentPage = ShellPage.Settings;

        // Act
        viewModel.OpenRepairCommand.Execute(null);

        // Assert
        Assert.Equal([Loc.Instance["Settings_RepairNotNeeded"]], dialogs.Informed);
        Assert.Equal(ShellPage.Settings, shell.CurrentPage);
    }

    [Fact]
    public async Task Open_repair_when_winget_is_broken_shows_repair_page()
    {
        // Arrange
        var probe = new FakeSystemProbe
        {
            WingetResult = new ProbeResult(false, "", "winget yok"),
            AppInstallerVersion = null,
        };
        var (viewModel, shell) = await CreateAsync(probe);
        shell.CurrentPage = ShellPage.Settings;

        // Act
        viewModel.OpenRepairCommand.Execute(null);

        // Assert
        Assert.Equal(ShellPage.Repair, shell.CurrentPage);
        Assert.Empty(dialogs.Informed);
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
