using System.Globalization;
using System.Reflection;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;
using KolayKur.Core.Queue;
using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Tests.Queue;

// Dil Loc.Instance üzerinde değiştiği için dil testleriyle aynı koleksiyonda çalışır.
[Collection("Localization")]
public sealed class OperationReasonTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Loc.Instance.SetLanguage("tr");
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    private static IEnumerable<string> AllReasonKeys() =>
        typeof(OperationReason)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

    private async Task<OperationItem> RunAsync(IWingetClient client)
    {
        var queue = new OperationQueue(client, new FileLog(logDirectory));
        var item = queue.Enqueue(OperationKind.Install, new PackageInfo("A.One", "A.One", "Y", null, "1.0"))!;
        await queue.WhenIdleAsync().WaitAsync(Timeout);
        return item;
    }

    [Fact]
    public void Every_reason_key_has_text_in_both_languages()
    {
        var keys = AllReasonKeys().ToList();

        Assert.NotEmpty(keys);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var language in new[] { "tr", "en" })
        {
            foreach (var key in keys)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(Loc.Resources.GetString(key, CultureInfo.GetCultureInfo(language))),
                    $"{language}: {key}");
            }
        }
    }

    [Fact]
    public async Task Client_exception_gets_the_unexpected_reason_and_keeps_the_technical_text()
    {
        var item = await RunAsync(new ScriptedClient(() => throw new InvalidOperationException("kaynak: ham-hata")));

        Assert.Equal(OperationStatus.Failed, item.Status);
        Assert.Equal(OperationReason.Unexpected, item.ReasonKey);
        Assert.Equal("kaynak: ham-hata", item.TechnicalDetail);
    }

    [Fact]
    public async Task Unreachable_catalog_gets_its_own_reason()
    {
        var item = await RunAsync(new ScriptedClient(() => throw new CatalogUnavailableException("connect failed")));

        Assert.Equal(OperationStatus.Failed, item.Status);
        Assert.Equal(OperationReason.CatalogUnavailable, item.ReasonKey);
    }

    [Fact]
    public async Task Invalid_client_status_fails_with_the_unexpected_reason()
    {
        var item = await RunAsync(new ScriptedClient(() => new OperationOutcome(OperationStatus.Running, null)));

        Assert.Equal(OperationStatus.Failed, item.Status);
        Assert.Equal(OperationReason.Unexpected, item.ReasonKey);
    }

    [Fact]
    public async Task Explicit_reason_from_the_client_is_kept()
    {
        var outcome = new OperationOutcome(OperationStatus.Failed, "InstallError (0x1)", OperationReason.InstallFailed);

        var item = await RunAsync(new ScriptedClient(() => outcome));

        Assert.Equal(OperationReason.InstallFailed, item.ReasonKey);
    }

    [Theory]
    [InlineData(OperationStatus.Failed, OperationReason.Unexpected)]
    [InlineData(OperationStatus.Cancelled, OperationReason.UserCancelled)]
    [InlineData(OperationStatus.NotFound, OperationReason.NotFound)]
    public async Task Missing_reason_falls_back_to_one_derived_from_the_status(OperationStatus status, string expected)
    {
        var item = await RunAsync(new ScriptedClient(() => new OperationOutcome(status, null)));

        Assert.Equal(expected, item.ReasonKey);
    }

    [Fact]
    public async Task Succeeded_item_has_no_reason()
    {
        var item = await RunAsync(new ScriptedClient(() => new OperationOutcome(OperationStatus.Succeeded, null)));

        Assert.Null(item.ReasonKey);
        Assert.Equal(string.Empty, item.DetailText);
    }

    [Fact]
    public async Task Detail_text_is_the_localized_reason_and_follows_the_language()
    {
        Loc.Instance.SetLanguage("tr");
        var outcome = new OperationOutcome(
            OperationStatus.Failed, "InstallError (error 0x8A150006, exit code 1603)", OperationReason.InstallFailed);
        var item = await RunAsync(new ScriptedClient(() => outcome));
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var turkish = item.DetailText;

        Loc.Instance.SetLanguage("en");

        Assert.Equal(Loc.Resources.GetString(OperationReason.InstallFailed, CultureInfo.GetCultureInfo("tr")), turkish);
        Assert.Equal(Loc.Resources.GetString(OperationReason.InstallFailed, CultureInfo.GetCultureInfo("en")), item.DetailText);
        Assert.NotEqual(turkish, item.DetailText);
        Assert.Contains(nameof(OperationItem.DetailText), changed);
        Assert.DoesNotContain("1603", item.DetailText, StringComparison.Ordinal);
        Assert.Equal("InstallError (error 0x8A150006, exit code 1603)", item.TechnicalDetail);
    }

    private sealed class ScriptedClient(Func<OperationOutcome> install) : IWingetClient
    {
        public Task<OperationOutcome> InstallAsync(
            string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
            Task.Run(install, ct);

        public Task<IReadOnlyList<PackageInfo>> SearchAsync(string query, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageInfo>> GetInstalledAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PackageInfo>> GetUpgradesAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> UpgradeAsync(
            string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<KolayKur.Core.Icons.IconHints> GetIconHintsAsync(string packageId, string? source, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<OperationOutcome> UninstallAsync(
            string packageId, IProgress<OperationProgress> progress, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
