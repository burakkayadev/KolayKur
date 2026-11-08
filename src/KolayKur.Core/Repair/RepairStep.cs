namespace KolayKur.Core.Repair;

/// <summary>Onarımın o an yaptığı iş. Arayüz bunları <c>Repair_Stage_*</c> metinlerine çevirir.</summary>
public enum RepairStage
{
    SearchingRelease,
    Downloading,
    ExtractingDependencies,
    InstallingAppInstaller,
    InstallingDependency,
    Registering,
}

/// <summary>
/// Onarım ilerlemesi. <see cref="Subject"/> işin konusudur (indirilen dosya veya kurulan bileşen adı),
/// <see cref="Percent"/> yalnızca indirmede (0-100) doludur. Kullanıcıya görünen metin burada değil, arayüzde üretilir.
/// </summary>
public sealed record RepairStep(RepairStage Stage, string? Subject = null, int? Percent = null);
