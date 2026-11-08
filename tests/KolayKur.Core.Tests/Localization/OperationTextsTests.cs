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
    public void Running_stage_is_translated_and_finished_items_show_the_localized_reason_never_the_raw_message()
    {
        Loc.Instance.SetLanguage("tr");

        Assert.Equal("İndiriliyor", OperationTexts.DetailText(OperationStatus.Running, ProgressStage.Downloading, null));
        Assert.Equal(string.Empty, OperationTexts.DetailText(OperationStatus.Running, "bilinmeyen", null));
        Assert.Equal(string.Empty, OperationTexts.DetailText(OperationStatus.Queued, null, null));
        Assert.Equal(string.Empty, OperationTexts.DetailText(OperationStatus.Succeeded, null, null));

        var failed = OperationTexts.DetailText(
            OperationStatus.Failed, "InstallError (error 0x1, exit code 1603)", OperationReason.InstallFailed);
        Assert.Equal(Loc.Instance[OperationReason.InstallFailed], failed);
        Assert.DoesNotContain("1603", failed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OperationStatus.Failed, OperationReason.Unexpected)]
    [InlineData(OperationStatus.Cancelled, OperationReason.UserCancelled)]
    [InlineData(OperationStatus.NotFound, OperationReason.NotFound)]
    public void Finished_status_without_a_reason_uses_the_default_for_that_status(OperationStatus status, string expected)
    {
        Assert.Equal(expected, OperationTexts.ReasonKeyFor(status, null));
        Assert.Equal(OperationReason.InstallFailed, OperationTexts.ReasonKeyFor(status, OperationReason.InstallFailed));
    }

    [Fact]
    public void Statuses_that_need_no_explanation_have_no_default_reason()
    {
        Assert.Null(OperationTexts.ReasonKeyFor(OperationStatus.Succeeded, null));
        Assert.Null(OperationTexts.ReasonKeyFor(OperationStatus.Running, null));
        Assert.Null(OperationTexts.ReasonKeyFor(OperationStatus.Queued, null));
    }
}
