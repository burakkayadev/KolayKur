using KolayKur.Core.Localization;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;

namespace KolayKur.Core.Tests.Support;

// Dialog metni Loc.Instance'tan geldiği için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class UnhandledErrorReporterTests : IDisposable
{
    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    private readonly FakeDialogService dialogs = new();

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    private FileLog Log => new(logDirectory);

    [Fact]
    public void Ui_error_is_logged_and_the_user_is_told_once()
    {
        var reporter = new UnhandledErrorReporter(Log, dialogs);

        reporter.ReportAndInform("UI iş parçacığı", new InvalidOperationException("arayuz-patladi-xyz"));

        Assert.Equal([Loc.Instance["Error_Unhandled"]], dialogs.Informed);
        var written = File.ReadAllText(Log.CurrentFile);
        Assert.Contains("arayuz-patladi-xyz", written, StringComparison.Ordinal);
        Assert.Contains("UI iş parçacığı", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_raised_while_the_dialog_is_open_are_only_logged()
    {
        var reporter = new UnhandledErrorReporter(Log, dialogs);
        dialogs.OnInform = _ => reporter.ReportAndInform("UI", new InvalidOperationException("ic-ice-hata"));

        reporter.ReportAndInform("UI", new InvalidOperationException("ilk-hata"));

        Assert.Single(dialogs.Informed);
        var written = File.ReadAllText(Log.CurrentFile);
        Assert.Contains("ilk-hata", written, StringComparison.Ordinal);
        Assert.Contains("ic-ice-hata", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_error_after_the_dialog_closed_is_shown_again()
    {
        var reporter = new UnhandledErrorReporter(Log, dialogs);

        reporter.ReportAndInform("UI", new InvalidOperationException("bir"));
        reporter.ReportAndInform("UI", new InvalidOperationException("iki"));

        Assert.Equal(2, dialogs.Informed.Count);
    }

    [Fact]
    public void Background_errors_are_logged_without_a_dialog()
    {
        var reporter = new UnhandledErrorReporter(Log, dialogs);

        reporter.ReportOnly("Görev", new InvalidOperationException("arka-plan-hatasi"));
        reporter.ReportOnly("AppDomain", "istisna olmayan nesne");

        Assert.Empty(dialogs.Informed);
        var written = File.ReadAllText(Log.CurrentFile);
        Assert.Contains("arka-plan-hatasi", written, StringComparison.Ordinal);
        Assert.Contains("istisna olmayan nesne", written, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_dialog_never_throws_out_of_the_handler()
    {
        var reporter = new UnhandledErrorReporter(Log, dialogs);
        dialogs.OnInform = _ => throw new InvalidOperationException("dialog-acilamadi");

        reporter.ReportAndInform("UI", new InvalidOperationException("asil-hata"));

        var written = File.ReadAllText(Log.CurrentFile);
        Assert.Contains("asil-hata", written, StringComparison.Ordinal);
        Assert.Contains("dialog-acilamadi", written, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_log_does_not_break_reporting()
    {
        var reporter = new UnhandledErrorReporter(null, dialogs);

        reporter.ReportAndInform("UI", new InvalidOperationException("gunluk-yok"));

        Assert.Single(dialogs.Informed);
    }
}
