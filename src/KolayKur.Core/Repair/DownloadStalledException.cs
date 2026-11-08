namespace KolayKur.Core.Repair;

/// <summary>İndirme sürerken uzun süre (bağlantı koptu/dondu) hiç veri gelmedi; yarım dosya silindi.</summary>
public sealed class DownloadStalledException(string fileName, Exception? innerException = null)
    : Exception($"{fileName} indirilirken uzun süre veri gelmedi.", innerException);
