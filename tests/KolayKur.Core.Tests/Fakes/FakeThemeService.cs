using KolayKur.Core.Support;

namespace KolayKur.Core.Tests.Fakes;

/// <summary>Uygulanan temaları sırayla kaydeder.</summary>
public sealed class FakeThemeService : IThemeService
{
    public List<AppTheme> Applied { get; } = [];

    public void Apply(AppTheme theme) => Applied.Add(theme);
}
