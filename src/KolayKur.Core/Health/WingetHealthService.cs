namespace KolayKur.Core.Health;

public enum WingetState { Healthy, NotInstalled, DependencyBroken, RegistrationBroken }

public sealed record WingetDiagnosis(WingetState State, string? BrokenDependency, string Detail);

public sealed class WingetHealthService(ISystemProbe probe)
{
    private static readonly TimeSpan WingetTimeout = TimeSpan.FromSeconds(15);

    public async Task<WingetDiagnosis> DiagnoseAsync(CancellationToken ct)
    {
        var winget = await probe.RunWingetVersionAsync(WingetTimeout, ct);
        if (winget.Succeeded)
        {
            return new WingetDiagnosis(WingetState.Healthy, null, winget.Output.Trim());
        }

        var wingetError = winget.Error ?? "";

        if (probe.GetAppInstallerVersion() is null)
        {
            return new WingetDiagnosis(WingetState.NotInstalled, null, "Uygulama Yükleyici (App Installer) kurulu değil.");
        }

        foreach (var dependency in probe.GetAppInstallerDependencies())
        {
            var registered = probe.GetRegisteredVersion(dependency.Name);
            if (registered is null)
            {
                return new WingetDiagnosis(
                    WingetState.DependencyBroken, dependency.Name, $"{dependency.Name} kayıtlı değil. {wingetError}".Trim());
            }

            if (registered < dependency.MinVersion)
            {
                return new WingetDiagnosis(
                    WingetState.DependencyBroken,
                    dependency.Name,
                    $"{dependency.Name} sürümü eski: {registered}, gereken en az {dependency.MinVersion}.");
            }
        }

        return new WingetDiagnosis(
            WingetState.RegistrationBroken, null, $"Bağımlılıklar tamam ama winget çalışmıyor. {wingetError}".Trim());
    }
}
