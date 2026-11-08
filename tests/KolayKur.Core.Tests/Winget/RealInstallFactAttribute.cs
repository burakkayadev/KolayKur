using Microsoft.Win32;

namespace KolayKur.Core.Tests.Winget;

/// <summary>
/// Makineye gerçekten kurulum yapan testler için kapı. Test yalnızca <c>KOLAYKUR_REAL_INSTALL=1</c> ise
/// ve <c>programName</c> adlı bir program makinede yüklü değilse çalışır; aksi halde atlanmış görünür.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RealInstallFactAttribute : FactAttribute
{
    public const string EnableVariable = "KOLAYKUR_REAL_INSTALL";

    public RealInstallFactAttribute(string programName)
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Skip = $"Gerçek kurulum testi kapalı: {EnableVariable}=1 ayarlanmalı.";
        }
        else if (IsProgramInstalled(programName))
        {
            Skip = $"{programName} bu makinede zaten yüklü; test onu silerdi.";
        }
    }

    /// <summary>Kaldır/Değiştir kayıtlarında adı verilen metinle başlayan bir program arar (winget kimliğinden bağımsız).</summary>
    internal static bool IsProgramInstalled(string programName)
    {
        string[] uninstallKeys =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            foreach (var path in uninstallKeys)
            {
                using var key = root.OpenSubKey(path);
                if (key is null)
                {
                    continue;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    using var entry = key.OpenSubKey(name);
                    var display = entry?.GetValue("DisplayName") as string;
                    if (display?.StartsWith(programName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
