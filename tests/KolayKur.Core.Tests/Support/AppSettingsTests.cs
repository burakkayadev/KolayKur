using KolayKur.Core.Support;

namespace KolayKur.Core.Tests.Support;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(directory, "settings.json");

    [Fact]
    public void Broken_settings_file_gives_defaults()
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{ bu json degil ][");

        // Act
        var settings = AppSettings.Load(SettingsPath);

        // Assert
        Assert.Equal("tr", settings.Language);
        Assert.False(settings.LicenseNoticeShown);
    }

    [Fact]
    public void Missing_settings_file_gives_defaults()
    {
        var settings = AppSettings.Load(SettingsPath);

        Assert.Equal(new AppSettings(), settings);
    }

    [Theory]
    [InlineData("{\"Language\":null}")]
    [InlineData("{\"Language\":\"\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Unusable_language_gives_default_language(string json)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, json);

        var settings = AppSettings.Load(SettingsPath);

        Assert.Equal("tr", settings.Language);
    }

    [Fact]
    public void Save_creates_directory_and_round_trips()
    {
        var saved = new AppSettings("en", LicenseNoticeShown: true);

        saved.Save(SettingsPath);
        var loaded = AppSettings.Load(SettingsPath);

        Assert.Equal(saved, loaded);
        Assert.Equal([SettingsPath], Directory.GetFiles(directory));
    }

    [Fact]
    public void Theme_defaults_to_system_when_missing()
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, "{\"Language\":\"en\",\"LicenseNoticeShown\":true}");

        // Act
        var settings = AppSettings.Load(SettingsPath);

        // Assert
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal("en", settings.Language);
        Assert.True(settings.LicenseNoticeShown);
    }

    [Theory]
    [InlineData("\"Purple\"")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("{\"x\":1}")]
    public void Unknown_theme_falls_back_to_system_and_keeps_other_values(string themeJson)
    {
        // Arrange
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, $"{{\"Language\":\"en\",\"Theme\":{themeJson},\"LicenseNoticeShown\":true}}");

        // Act
        var settings = AppSettings.Load(SettingsPath);

        // Assert
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Equal("en", settings.Language);
        Assert.True(settings.LicenseNoticeShown);
    }

    [Fact]
    public void Theme_round_trips_as_text()
    {
        // Act
        new AppSettings(Theme: AppTheme.Dark).Save(SettingsPath);

        // Assert
        Assert.Contains("\"Dark\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.Equal(AppTheme.Dark, AppSettings.Load(SettingsPath).Theme);
    }

    [Fact]
    public void Save_replaces_existing_file()
    {
        new AppSettings("en", true).Save(SettingsPath);

        new AppSettings("tr", false).Save(SettingsPath);

        Assert.Equal(new AppSettings("tr", false), AppSettings.Load(SettingsPath));
    }
}
