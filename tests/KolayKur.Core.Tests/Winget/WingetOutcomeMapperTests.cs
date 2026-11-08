using KolayKur.Core.Models;
using KolayKur.Core.Winget;
using Microsoft.Management.Deployment;

namespace KolayKur.Core.Tests.Winget;

public class WingetOutcomeMapperTests
{
    private const int NoError = 0;
    private const int ErrorCancelledHresult = unchecked((int)0x800704C7);
    private const int WingetInstallCancelledByUser = unchecked((int)0x8A15010C);
    private const int WingetCtrlSignalReceived = unchecked((int)0x8A150005);
    private const int WingetShellExecFailed = unchecked((int)0x8A150006);
    private const uint ErrorCancelledExitCode = 1223;
    private const uint MsiUserExitCode = 1602;

    [Fact]
    public void Ok_status_is_success()
    {
        var outcome = WingetOutcomeMapper.MapInstall(InstallResultStatus.Ok, NoError, 0);

        Assert.Equal(OperationStatus.Succeeded, outcome.Status);
    }

    [Theory]
    [InlineData(ErrorCancelledHresult, 0u)]
    [InlineData(WingetInstallCancelledByUser, 0u)]
    [InlineData(WingetCtrlSignalReceived, 0u)]
    [InlineData(WingetShellExecFailed, ErrorCancelledExitCode)]
    [InlineData(WingetShellExecFailed, MsiUserExitCode)]
    public void Declined_elevation_or_user_cancel_maps_to_cancelled(int extendedErrorCode, uint installerErrorCode)
    {
        var outcome = WingetOutcomeMapper.MapInstall(InstallResultStatus.InstallError, extendedErrorCode, installerErrorCode);

        Assert.Equal(OperationStatus.Cancelled, outcome.Status);
    }

    [Theory]
    [InlineData(InstallResultStatus.InstallError)]
    [InlineData(InstallResultStatus.DownloadError)]
    [InlineData(InstallResultStatus.NoApplicableInstallers)]
    [InlineData(InstallResultStatus.NoApplicableUpgrade)]
    [InlineData(InstallResultStatus.BlockedByPolicy)]
    [InlineData(InstallResultStatus.PackageAgreementsNotAccepted)]
    public void Other_install_errors_map_to_failed_with_details(InstallResultStatus status)
    {
        var outcome = WingetOutcomeMapper.MapInstall(status, WingetShellExecFailed, 5);

        Assert.Equal(OperationStatus.Failed, outcome.Status);
        Assert.Contains(status.ToString(), outcome.Message);
        Assert.Contains("8A150006", outcome.Message);
    }

    [Fact]
    public void Uninstall_ok_is_success_and_cancel_is_cancelled()
    {
        var ok = WingetOutcomeMapper.MapUninstall(UninstallResultStatus.Ok, NoError, 0);
        var cancelled = WingetOutcomeMapper.MapUninstall(
            UninstallResultStatus.UninstallError, ErrorCancelledHresult, 0);
        var failed = WingetOutcomeMapper.MapUninstall(UninstallResultStatus.UninstallError, WingetShellExecFailed, 3);

        Assert.Equal(OperationStatus.Succeeded, ok.Status);
        Assert.Equal(OperationStatus.Cancelled, cancelled.Status);
        Assert.Equal(OperationStatus.Failed, failed.Status);
    }

    [Fact]
    public void Plain_failure_codes_are_not_cancellation()
    {
        Assert.False(WingetOutcomeMapper.IsCancellation(WingetShellExecFailed, 1));
        Assert.True(WingetOutcomeMapper.IsCancellation(ErrorCancelledHresult, 0));
    }
}
