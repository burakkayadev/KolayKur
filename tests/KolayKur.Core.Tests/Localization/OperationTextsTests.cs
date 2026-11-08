using System.Globalization;
using System.Reflection;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;

namespace KolayKur.Core.Tests.Localization;

// Loc.Instance süreçte tektir; dil değiştiren testlerle aynı koleksiyonda sırayla çalışır.
[Collection("Localization")]
public sealed class OperationTextsTests
{
    private static readonly string[] Languages = ["tr", "en"];

    private static IEnumerable<string> AllStageKeys() =>
        typeof(ProgressStage)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => OperationTexts.StageKey((string)field.GetRawConstantValue()!));

    [Fact]
    public void Every_status_kind_and_stage_has_text_in_both_languages()
    {
        var keys = Enum.GetValues<OperationStatus>().Select(OperationTexts.StatusKey)
            .Concat(Enum.GetValues<OperationKind>().Select(OperationTexts.KindKey))
            .Concat(AllStageKeys())
            .ToList();

        Assert.NotEmpty(AllStageKeys());
        foreach (var language in Languages)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var key in keys)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(Loc.Resources.GetString(key, culture)), $"{language}: {key}");
            }
        }
    }

    [Fact]
    public void Running_stage_is_translated_but_final_message_is_shown_as_is()
    {
        Loc.Instance.SetLanguage("tr");

        Assert.Equal("İndiriliyor", OperationTexts.MessageText(OperationStatus.Running, ProgressStage.Downloading));
        Assert.Equal("downloading", OperationTexts.MessageText(OperationStatus.Failed, "downloading"));
        Assert.Equal("hata ayrıntısı", OperationTexts.MessageText(OperationStatus.Failed, "hata ayrıntısı"));
        Assert.Equal("bilinmeyen", OperationTexts.MessageText(OperationStatus.Running, "bilinmeyen"));
        Assert.Equal(string.Empty, OperationTexts.MessageText(OperationStatus.Queued, null));
    }
}
