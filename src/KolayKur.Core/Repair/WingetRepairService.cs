using System.Globalization;
using KolayKur.Core.Health;
using KolayKur.Core.Support;

namespace KolayKur.Core.Repair;

public sealed class WingetRepairService(IRepairActions actions, WingetHealthService health, FileLog log)
{
    public async Task<WingetDiagnosis> RepairAsync(
        WingetDiagnosis diagnosis, IProgress<string> progress, CancellationToken ct)
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
        return result;
    }

    private async Task InstallFromScratchAsync(IProgress<string> progress, CancellationToken ct)
    {
        var release = await GetReleaseAsync(progress, ct);
        var dependenciesZip = await DownloadAsync(release.Dependencies, progress, ct);
        var appInstaller = await DownloadAsync(release.AppInstaller, progress, ct);

        progress.Report("Bağımlılıklar çıkarılıyor.");
        var dependencyPaths = actions.ExtractDependencies(dependenciesZip);

        progress.Report("Uygulama Yükleyici kuruluyor.");
        await actions.AddPackageAsync(appInstaller, dependencyPaths, ct);
    }

    private async Task RepairDependencyAsync(
        WingetDiagnosis diagnosis, IProgress<string> progress, CancellationToken ct)
    {
        var brokenDependency = diagnosis.BrokenDependency
            ?? throw new InvalidOperationException("Bozuk bağımlılığın adı bilinmiyor.");

        var release = await GetReleaseAsync(progress, ct);
        var dependenciesZip = await DownloadAsync(release.Dependencies, progress, ct);

        progress.Report("Bağımlılıklar çıkarılıyor.");
        var prefix = brokenDependency + "_";
        var matches = actions.ExtractDependencies(dependenciesZip)
            .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"{brokenDependency} paketi indirilen bağımlılıklar içinde yok.");
        }

        progress.Report($"{brokenDependency} kuruluyor.");
        foreach (var path in matches)
        {
            await actions.AddPackageAsync(path, [], ct);
        }

        await RegisterAsync(progress, ct);
    }

    private async Task RegisterAsync(IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Uygulama Yükleyici kaydediliyor.");
        await actions.RegisterAppInstallerAsync(ct);
    }

    private async Task<WingetRelease> GetReleaseAsync(IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Son winget sürümü aranıyor.");
        var release = await actions.GetLatestReleaseAsync(ct);
        log.Info($"Son winget sürümü: {release.Tag}");
        return release;
    }

    private async Task<string> DownloadAsync(RepairFile file, IProgress<string> progress, CancellationToken ct)
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
            progress.Report(string.Create(CultureInfo.InvariantCulture, $"{file.Name} indiriliyor: %{percent}"));
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
