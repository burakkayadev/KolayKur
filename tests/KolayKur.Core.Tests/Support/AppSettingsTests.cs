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
    public void Save_replaces_existing_file()
    {
        new AppSettings("en", true).Save(SettingsPath);

        new AppSettings("tr", false).Save(SettingsPath);

        Assert.Equal(new AppSettings("tr", false), AppSettings.Load(SettingsPath));
    }
}
