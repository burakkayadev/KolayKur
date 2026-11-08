using System.Globalization;
using KolayKur.Core.Models;
using Microsoft.Management.Deployment;

namespace KolayKur.Core.Winget;

/// <summary>
/// winget sonuç kodlarını <see cref="OperationOutcome"/> değerine çevirir.
/// "Cancelled" eşlemesi winget-cli kaynağından bilinen kodlara dayanır; gerçek bir izin reddiyle doğrulanmamıştır.
/// </summary>
internal static class WingetOutcomeMapper
{
    // Windows ERROR_CANCELLED (1223) HRESULT biçimi: kullanıcı izin (UAC) penceresine "Hayır" dediğinde ShellExecute döndürür.
    private const int ErrorCancelledHresult = unchecked((int)0x800704C7);

    // APPINSTALLER_CLI_ERROR_CTRL_SIGNAL_RECEIVED ve APPINSTALLER_CLI_ERROR_INSTALL_CANCELLED_BY_USER (winget-cli returnCodes.md).
    private const int WingetCtrlSignalReceived = unchecked((int)0x8A150005);
    private const int WingetInstallCancelledByUser = unchecked((int)0x8A15010C);

    // Yükleyici çıkış kodları: ERROR_CANCELLED ve MSI ERROR_INSTALL_USEREXIT.
    private const uint ExitCodeCancelled = 1223;
    private const uint ExitCodeMsiUserExit = 1602;

    /// <param name="kind">Kurulum ve güncelleme aynı winget çağrısını kullanır; neden metni için hangisi olduğu gerekir.</param>
    public static OperationOutcome MapInstall(
        InstallResultStatus status, int extendedErrorCode, uint installerErrorCode, OperationKind kind = OperationKind.Install)
    {
        var failedReason = kind == OperationKind.Upgrade ? OperationReason.UpgradeFailed : OperationReason.InstallFailed;
        if (status == InstallResultStatus.NoApplicableUpgrade)
        {
            failedReason = OperationReason.UpgradeNotNeeded;
        }

        return Map(status == InstallResultStatus.Ok, status.ToString(), extendedErrorCode, installerErrorCode, failedReason);
    }

    public static OperationOutcome MapUninstall(UninstallResultStatus status, int extendedErrorCode, uint uninstallerErrorCode) =>
        Map(
            status == UninstallResultStatus.Ok,
            status.ToString(),
            extendedErrorCode,
            uninstallerErrorCode,
            OperationReason.UninstallFailed);

    public static bool IsCancellation(int extendedErrorCode, uint installerErrorCode) =>
        extendedErrorCode is ErrorCancelledHresult or WingetCtrlSignalReceived or WingetInstallCancelledByUser
        || installerErrorCode is ExitCodeCancelled or ExitCodeMsiUserExit;

    // Message dilden bağımsız teknik ayrıntıdır (günlük/ipucu); kullanıcıya ReasonKey'in metni gösterilir.
    private static OperationOutcome Map(
        bool succeeded, string statusName, int extendedErrorCode, uint installerErrorCode, string failedReason)
    {
        var details = string.Create(
            CultureInfo.InvariantCulture,
            $"{statusName} (error 0x{extendedErrorCode:X8}, exit code {installerErrorCode})");

        if (succeeded)
        {
            return new OperationOutcome(OperationStatus.Succeeded, null);
        }

        return IsCancellation(extendedErrorCode, installerErrorCode)
            ? new OperationOutcome(OperationStatus.Cancelled, details, OperationReason.UserCancelled)
            : new OperationOutcome(OperationStatus.Failed, details, failedReason);
    }
}
