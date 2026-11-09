using KolayKur.Core.Models;
using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Icons;

/// <summary>
/// Bir paketin simgesini bulur, doğrular ve diske önbelleğe alır; sonuç yerel bir PNG yoludur.
/// Sıra: katalog simgesi, kurulu programın kendi simgesi, uygulamanın sitesi. Hiçbiri yoksa <c>null</c> (arayüz harfi gösterir).
/// <see cref="GetIconPathAsync"/> hata atmaz; ağ, COM ve çözme hataları "simge yok" gibi davranır (kullanıcıya gösterilmez).
/// Aynı paket için eşzamanlı istekler tek iş olarak birleşir; bütün çağıranlar vazgeçerse iş iptal edilir.
/// </summary>
public sealed class IconService(
    IWingetClient winget,
    IIconFetcher fetcher,
    IInstalledIconReader installedReader,
    IIconDecoder decoder,
    IconCache cache,
    FileLog? log = null)
{
    private const int MaxCatalogCandidates = 3;

    private readonly Dictionary<string, Job> jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly SemaphoreSlim downloads = new(IconLimits.MaxConcurrentDownloads);
    private readonly SemaphoreSlim hintReads = new(IconLimits.MaxConcurrentHints);
    private int loggedFailures;

    public async Task<string?> GetIconPathAsync(PackageInfo package, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // Önbellek denetimi dosya sistemine dokunur: çağıran arayüz iş parçacığındaysa onu bekletmemek için havuzda yapılır.
            var (cached, negative) = await Task.Run(() => (cache.TryGet(package.Id), cache.IsNegative(package.Id)), ct).ConfigureAwait(false);
            if (cached is not null || negative)
            {
                return cached;
            }

            var job = Join(package);
            try
            {
                return await job.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                Leave(package.Id, job);
            }
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                LogOnce(ex);
            }

            return null;
        }
    }

    private Job Join(PackageInfo package)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(package.Id, out var job))
            {
                job = new Job();
                jobs[package.Id] = job;
                var token = job.Cts.Token;
                job.Task = Task.Run(() => FindAsync(package, token), token);
            }

            job.Waiters++;
            return job;
        }
    }

    private void Leave(string id, Job job)
    {
        lock (gate)
        {
            if (--job.Waiters > 0)
            {
                return;
            }

            if (jobs.TryGetValue(id, out var current) && ReferenceEquals(current, job))
            {
                jobs.Remove(id);
            }

            if (!job.Task.IsCompleted)
            {
                job.Cts.Cancel();
            }
        }
    }

    private async Task<string?> FindAsync(PackageInfo package, CancellationToken ct)
    {
        var transientFailure = false;
        try
        {
            var hints = await ReadHintsAsync(package, ct).ConfigureAwait(false);
            if (hints is null)
            {
                transientFailure = true;
                return null;
            }

            foreach (var source in Sources(hints))
            {
                var bytes = await TryGetBytesAsync(source, ct).ConfigureAwait(false);
                if (bytes.Failed)
                {
                    transientFailure = true;
                }

                if (bytes.Data is null)
                {
                    continue;
                }

                var png = await DecodeAsync(bytes.Data, ct).ConfigureAwait(false);
                if (png is not null)
                {
                    return cache.Put(package.Id, source.Key, png);
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (!transientFailure && !ct.IsCancellationRequested && cache.TryGet(package.Id) is null)
            {
                cache.PutNegative(package.Id);
            }
        }
    }

    /// <summary>Okunamazsa (COM hatası) <c>null</c>: geçici sayılır, "simge yok" önbelleğe alınmaz.</summary>
    private async Task<IconHints?> ReadHintsAsync(PackageInfo package, CancellationToken ct)
    {
        await hintReads.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await winget.GetIconHintsAsync(package.Id, package.Source, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOnce(ex);
            return null;
        }
        finally
        {
            hintReads.Release();
        }
    }

    private static IEnumerable<IconSource> Sources(IconHints hints)
    {
        foreach (var url in hints.CatalogIcons.Take(MaxCatalogCandidates))
        {
            yield return new IconSource(url.AbsoluteUri, url, null);
        }

        if (!string.IsNullOrWhiteSpace(hints.InstalledIconSource))
        {
            yield return new IconSource("installed:" + hints.InstalledIconSource, null, hints.InstalledIconSource);
        }

        if (hints.Website is not null)
        {
            foreach (var url in IconSiteRules.WebsiteIconCandidates(hints.Website))
            {
                yield return new IconSource(url.AbsoluteUri, url, null);
            }
        }
    }

    private async Task<(byte[]? Data, bool Failed)> TryGetBytesAsync(IconSource source, CancellationToken ct)
    {
        try
        {
            if (source.InstalledSource is not null)
            {
                return (await installedReader.ReadAsync(source.InstalledSource, ct).ConfigureAwait(false), false);
            }

            await downloads.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return (await fetcher.FetchAsync(source.Url!, ct).ConfigureAwait(false), false);
            }
            finally
            {
                downloads.Release();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ağ kesintisi gibi geçici hata: sonraki kaynağa geçilir, "simge yok" kaydedilmez.
            LogOnce(ex);
            return (null, true);
        }
    }

    private async Task<byte[]?> DecodeAsync(byte[] data, CancellationToken ct)
    {
        try
        {
            return await decoder.ToPngAsync(data, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOnce(ex);
            return null;
        }
    }

    // Her kart için günlüğü doldurmamak için yalnızca ilk birkaç hata yazılır.
    private void LogOnce(Exception ex)
    {
        if (Interlocked.Increment(ref loggedFailures) <= 3)
        {
            log.TryError("Simge bulunurken hata (simge atlandı; bundan sonrakiler yazılmaz).", ex);
        }
    }

    private sealed record IconSource(string Key, Uri? Url, string? InstalledSource);

    private sealed class Job
    {
        public CancellationTokenSource Cts { get; } = new();

        public Task<string?> Task { get; set; } = System.Threading.Tasks.Task.FromResult<string?>(null);

        public int Waiters { get; set; }
    }
}
