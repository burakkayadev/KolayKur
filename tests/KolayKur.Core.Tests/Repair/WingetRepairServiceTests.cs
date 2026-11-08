using KolayKur.Core.Health;
using KolayKur.Core.Repair;
using KolayKur.Core.Support;
using KolayKur.Core.Tests.Fakes;

namespace KolayKur.Core.Tests.Repair;

public sealed class WingetRepairServiceTests : IDisposable
{
    private readonly string logDirectory =
        Path.Combine(Path.GetTempPath(), "KolayKurTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(logDirectory))
        {
            Directory.Delete(logDirectory, recursive: true);
        }
    }

    private Task<WingetDiagnosis> RepairAsync(FakeRepairActions actions, WingetDiagnosis diagnosis)
    {
        // Onarımdan sonra winget çalışıyor: yeni teşhis Healthy döner.
        var health = new WingetHealthService(new FakeSystemProbe());
        var service = new WingetRepairService(actions, health, new FileLog(logDirectory));
        return service.RepairAsync(diagnosis, new Progress<string>(), CancellationToken.None);
    }

    [Fact]
    public async Task Healthy_does_nothing()
    {
        var actions = new FakeRepairActions();
        var healthy = new WingetDiagnosis(WingetState.Healthy, null, "v1.29.380");

        var result = await RepairAsync(actions, healthy);

        Assert.Empty(actions.Calls);
        Assert.Equal(WingetState.Healthy, result.State);
    }

    [Fact]
    public async Task NotInstalled_installs_dependencies_then_app_installer()
    {
        var actions = new FakeRepairActions();

        await RepairAsync(actions, new WingetDiagnosis(WingetState.NotInstalled, null, "yok"));

        Assert.Equal(
            ["Release", "Download:Dependencies", "Download:AppInstaller", "Extract", "Add:AppInstaller"],
            actions.Calls);
        Assert.Equal(FakeRepairActions.ExtractedPackages, actions.AddedDependencies.Single());
    }

    [Fact]
    public async Task DependencyBroken_adds_only_that_framework_then_registers()
    {
        var actions = new FakeRepairActions();

        await RepairAsync(
            actions,
            new WingetDiagnosis(WingetState.DependencyBroken, FakeRepairActions.WindowsAppRuntime, "eksik"));

        Assert.Equal(
            ["Release", "Download:Dependencies", "Extract", "Add:Microsoft.WindowsAppRuntime.1.8", "Register"],
            actions.Calls);
        Assert.Empty(actions.AddedDependencies.Single());
    }

    [Fact]
    public async Task DependencyBroken_without_matching_package_fails_clearly()
    {
        var actions = new FakeRepairActions();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RepairAsync(
            actions, new WingetDiagnosis(WingetState.DependencyBroken, "Microsoft.Yok.9.9", "eksik")));

        Assert.Contains("Microsoft.Yok.9.9", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Register", actions.Calls);
    }

    [Fact]
    public async Task RegistrationBroken_only_registers()
    {
        var actions = new FakeRepairActions();

        await RepairAsync(actions, new WingetDiagnosis(WingetState.RegistrationBroken, null, "kayıt"));

        Assert.Equal(["Register"], actions.Calls);
    }

    [Fact]
    public async Task Repair_returns_fresh_diagnosis()
    {
        var actions = new FakeRepairActions();

        var result = await RepairAsync(actions, new WingetDiagnosis(WingetState.NotInstalled, null, "yok"));

        Assert.Equal(WingetState.Healthy, result.State);
    }
}
