namespace KolayKur.Core.Winget;

public enum WinmdStatus
{
    /// <summary>Dosya zaten programın yanındaydı; dokunulmadı.</summary>
    AlreadyPresent,

    /// <summary>Dosya yoktu, gömülü kaynaktan çıkarıldı.</summary>
    Extracted,

    /// <summary>Dosya yoktu ve klasöre yazılamadı (yazma izni yok, disk hatası vb.).</summary>
    ExtractionFailed,
}

/// <param name="Status">Sonuç.</param>
/// <param name="Error">Yalnızca <see cref="WinmdStatus.ExtractionFailed"/> ise: yazma hatasının kendisi.</param>
public sealed record WinmdBootstrapResult(WinmdStatus Status, Exception? Error = null);

/// <summary>
/// winget COM nesneleri, programın yanındaki <c>Microsoft.Management.Deployment.winmd</c> dosyası olmadan oluşturulamaz
/// (<c>0x80073D54</c>). Tek dosya yayınında gömülü bir winmd diske yazılmaz; bu sınıf dosyayı, COM'a ilk dokunuştan önce
/// gömülü kaynaktan programın yanına çıkarır. WPF'ten bağımsızdır; kaynak, çağıran tarafından akış olarak verilir.
/// </summary>
public static class WinmdBootstrapper
{
    internal const string WinmdFileName = "Microsoft.Management.Deployment.winmd";

    /// <summary>
    /// <paramref name="directory"/> içinde winmd yoksa <paramref name="openResource"/> akışından çıkarır. Önce aynı klasörde
    /// geçici dosyaya yazar, uzunluğu kaynakla karşılaştırır, sonra adını değiştirir; yarım dosya kalmaz.
    /// </summary>
    /// <param name="openResource">Gömülü kaynağı açar; kaynak derlemede yoksa <c>null</c> döner (bu bir yapım hatasıdır).</param>
    /// <exception cref="InvalidOperationException">Kaynak derlemede yok; kullanıcının çözebileceği bir durum değil.</exception>
    public static WinmdBootstrapResult EnsureExtracted(string directory, Func<Stream?> openResource)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(openResource);

        var target = Path.Combine(directory, WinmdFileName);
        if (File.Exists(target))
        {
            return new WinmdBootstrapResult(WinmdStatus.AlreadyPresent);
        }

        using var source = openResource()
            ?? throw new InvalidOperationException($"{WinmdFileName} programa gömülü değil; yapım hatası.");

        var temp = Path.Combine(directory, $"{WinmdFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            Write(temp, source);
            if (source.CanSeek && new FileInfo(temp).Length != source.Length)
            {
                throw new IOException($"{WinmdFileName} eksik yazıldı.");
            }

            // İki program aynı anda açılırsa ikincisi dosyayı hazır bulur; üzerine yazılmaz.
            File.Move(temp, target, overwrite: false);
            return new WinmdBootstrapResult(WinmdStatus.Extracted);
        }
        catch (IOException) when (File.Exists(target))
        {
            return new WinmdBootstrapResult(WinmdStatus.AlreadyPresent);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new WinmdBootstrapResult(WinmdStatus.ExtractionFailed, ex);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void Write(string path, Stream source)
    {
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(output);
        output.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Geçici dosya silinemiyorsa yapılacak bir şey yok; asıl sonuç zaten döndü.
            System.Diagnostics.Trace.TraceWarning($"Geçici dosya silinemedi: {path}: {ex.Message}");
        }
    }
}
