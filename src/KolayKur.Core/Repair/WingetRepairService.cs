using KolayKur.Core.Health;
using KolayKur.Core.Support;

namespace KolayKur.Core.Repair;

public sealed class WingetRepairService(IRepairActions actions, WingetHealthService health, FileLog log)
{
    public async Task<WingetDiagnosis> RepairAsync(
        WingetDiagnosis diagnosis, IProgress<RepairStep> progress, CancellationToken ct)
    {
        if (diagnosis.State == WingetState.Healthy)
        {
            log.Info("Winget sağlıklı, onarım gerekmiyor.");
            return diagnosis;
        }

        log.Info($"Onarım başlıyor: {diagnosis.State} {diagnosis.BrokenDependency}".TrimEnd());
        try
        {
            switch (diagnosis.State)
            {
                case WingetState.NotInstalled:
                    await InstallFromScratchAsync(progress, ct);
                    break;
                case WingetState.DependencyBroken:
                    await RepairDependencyAsync(diagnosis, progress, ct);
                    break;
                case WingetState.RegistrationBroken:
                    await RegisterAsync(progress, ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            log.Info("Onarım iptal edildi.");
            throw;
        }
        catch (Exception ex)
        {
            log.Error("Onarım başarısız.", ex);
            throw;
        }

        var result = await health.DiagnoseAsync(ct);
        log.Info($"Onarım bitti: {result.State}");
        if (result.State == WingetState.Healthy)
        {
            CleanUpDownloads();
        }

        return result;
    }

    // İndirilenler yüzlerce MB tutar; başarıdan sonra silinir. Silinemezse onarım yine başarılıdır, yalnızca günlüğe yazılır.
    private void CleanUpDownloads()
    {
        try
        {
            actions.CleanupDownloads();
            log.TryInfo("İndirilen onarım dosyaları silindi.");
        }
        catch (Exception ex)
        {
            log.TryError("İndirilen onarım dosyaları silinemedi.", ex);
        }
    }

    private async Task InstallFromScratchAsync(IProgress<RepairStep> progress, CancellationToken ct)
    {
        var release = await GetReleaseAsync(progress, ct);
        var dependenciesZip = await DownloadAsync(release.Dependencies, progress, ct);
        var appInstaller = await DownloadAsync(release.AppInstaller, progress, ct);

        progress.Report(new RepairStep(RepairStage.ExtractingDependencies));
        var dependencyPaths = actions.ExtractDependencies(dependenciesZip);

        progress.Report(new RepairStep(RepairStage.InstallingAppInstaller));
        await actions.AddPackageAsync(appInstaller, dependencyPaths, ct);
    }

    private async Task RepairDependencyAsync(
        WingetDiagnosis diagnosis, IProgress<RepairStep> progress, CancellationToken ct)
    {
        var brokenDependency = diagnosis.BrokenDependency
            ?? throw new InvalidOperationException("Bozuk bağımlılığın adı bilinmiyor.");

        var release = await GetReleaseAsync(progress, ct);
        var dependenciesZip = await DownloadAsync(release.Dependencies, progress, ct);

        progress.Report(new RepairStep(RepairStage.ExtractingDependencies));
        var prefix = brokenDependency + "_";
        var matches = actions.ExtractDependencies(dependenciesZip)
            .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"{brokenDependency} paketi indirilen bağımlılıklar içinde yok.");
        }

        progress.Report(new RepairStep(RepairStage.InstallingDependency, brokenDependency));
        foreach (var path in matches)
        {
            await actions.AddPackageAsync(path, [], ct);
        }

        await RegisterAsync(progress, ct);
    }

    private async Task RegisterAsync(IProgress<RepairStep> progress, CancellationToken ct)
    {
        progress.Report(new RepairStep(RepairStage.Registering));
        await actions.RegisterAppInstallerAsync(ct);
    }

    private async Task<WingetRelease> GetReleaseAsync(IProgress<RepairStep> progress, CancellationToken ct)
    {
        progress.Report(new RepairStep(RepairStage.SearchingRelease));
        var release = await actions.GetLatestReleaseAsync(ct);
        log.Info($"Son winget sürümü: {release.Tag}");
        return release;
    }

    private async Task<string> DownloadAsync(RepairFile file, IProgress<RepairStep> progress, CancellationToken ct)
    {
        var lastPercent = -1;
        var downloadProgress = new InlineProgress<double>(fraction =>
        {
            var percent = (int)(fraction * 100);
            if (percent == lastPercent)
            {
                return;
            }

            lastPercent = percent;
            progress.Report(new RepairStep(RepairStage.Downloading, file.Name, percent));
        });

        var path = await actions.DownloadAsync(file, downloadProgress, ct);
        log.Info($"İndirildi ve doğrulandı: {path}");
        return path;
    }

    // Progress<T> geri çağrıyı havuz iş parçacığına bırakır; sıra bozulmasın diye eşzamanlı çağırır.
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
