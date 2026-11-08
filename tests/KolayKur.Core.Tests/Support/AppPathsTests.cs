using KolayKur.Core.Support;

namespace KolayKur.Core.Tests.Support;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DataDirEnvironmentCollection
{
    public const string Name = "KOLAYKUR_DATA_DIR environment variable";
}

[Collection(DataDirEnvironmentCollection.Name)]
public sealed class AppPathsTests
{
    private const string Variable = "KOLAYKUR_DATA_DIR";

    private static void WithOverride(string? value, Action body)
    {
        var previous = Environment.GetEnvironmentVariable(Variable);
        try
        {
            Environment.SetEnvironmentVariable(Variable, value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, previous);
        }
    }

    [Fact]
    public void Root_uses_data_dir_override_when_set()
    {
        // Arrange
        var directory = Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

        WithOverride(directory, () =>
        {
            // Act + Assert
            Assert.Equal(directory, AppPaths.Root);
            Assert.Equal(Path.Combine(directory, "logs"), AppPaths.Logs);
            Assert.Equal(Path.Combine(directory, "downloads"), AppPaths.Downloads);
            Assert.Equal(Path.Combine(directory, "settings.json"), AppPaths.SettingsFile);
        });
    }

    [Fact]
    public void Root_ignores_relative_override()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KolayKur");

        WithOverride("goreli", () => Assert.Equal(expected, AppPaths.Root));
    }
}
