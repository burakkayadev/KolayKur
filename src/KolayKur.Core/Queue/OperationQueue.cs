using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using KolayKur.Core.Models;
using KolayKur.Core.Support;
using KolayKur.Core.Winget;

namespace KolayKur.Core.Queue;

public sealed record BatchSummary(int Succeeded, int Failed, int Cancelled, int NotFound);

/// <summary>
/// İşlemleri tek arka plan döngüsünde, eklenme sırasıyla ve birer birer çalıştırır. Başarısız, iptal edilen veya
/// bulunamayan bir işlem sıradakini durdurmaz. <see cref="Items"/>, <see cref="IsBusy"/> bildirimleri, öğelerin
/// özellikleri ve <see cref="BatchCompleted"/> kurucuda yakalanan <see cref="SynchronizationContext"/> üzerinde
/// (bağlam yoksa çağıran iş parçacığında) tetiklenir. <see cref="Enqueue"/> her iş parçacığından çağrılabilir.
/// </summary>
public sealed class OperationQueue : ObservableObject
{
    private readonly IWingetClient client;
    private readonly FileLog log;
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly object gate = new();
    private readonly ObservableCollection<OperationItem> items = [];

    // Aşağıdakilerin hepsi gate kilidiyle korunur.
    private readonly Queue<OperationItem> pending = new();
    private readonly List<OperationItem> unfinished = [];
    private TaskCompletionSource idle = CompletedIdle();
    private bool loopRunning;
    private int succeeded;
    private int failed;
    private int cancelled;
    private int notFound;

    public OperationQueue(IWingetClient client, FileLog log)
    {
        this.client = client;
        this.log = log;
        Items = new ReadOnlyObservableCollection<OperationItem>(items);
    }

    public ReadOnlyObservableCollection<OperationItem> Items { get; }

    public bool IsBusy
    {
        get
        {
            lock (gate)
            {
                return loopRunning;
            }
        }
    }

    /// <summary>Kuyruk boşalınca bir kez tetiklenir; yalnızca o toplu işin (kuyruk son boşaldığından beri) sonuçlarını sayar.</summary>
    public event EventHandler<BatchSummary>? BatchCompleted;

    /// <summary>
    /// İşlemi kuyruğa ekler. Aynı kimlikli (büyük/küçük harf ayırmaz) bitmemiş bir işlem varsa <c>null</c> döner.
    /// </summary>
    public OperationItem? Enqueue(OperationKind kind, PackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var item = new OperationItem(kind, package, context);
        bool startLoop;
        lock (gate)
        {
            if (unfinished.Exists(other => string.Equals(other.Package.Id, package.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            unfinished.Add(item);
            pending.Enqueue(item);
            ContextMarshal.Post(context, () => items.Add(item));

            startLoop = !loopRunning;
            if (startLoop)
            {
                loopRunning = true;
                idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                NotifyIsBusyChanged();
            }
        }

        TryLog(() => log.Info($"Sıraya eklendi: {kind} {package.Id}"));
        if (startLoop)
        {
            _ = Task.Run(DrainAsync);
        }

        return item;
    }

    /// <summary>Kuyruk boş ve hiçbir işlem çalışmıyorken (<see cref="BatchCompleted"/> tetiklendikten sonra) tamamlanır.</summary>
    public Task WhenIdleAsync()
    {
        lock (gate)
        {
            return idle.Task;
        }
    }

    private static TaskCompletionSource CompletedIdle()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            OperationItem? next;
            lock (gate)
            {
                next = pending.TryDequeue(out var dequeued) ? dequeued : null;
            }

            if (next is not null)
            {
                await ProcessAsync(next).ConfigureAwait(false);
            }
            else if (await TryFinishBatchAsync().ConfigureAwait(false))
            {
                return;
            }
        }
    }

    private async Task ProcessAsync(OperationItem item)
    {
        var package = item.Package;
        item.MarkRunning();
        TryLog(() => log.Info($"İşlem başladı: {item.Kind} {package.Id}"));

        OperationOutcome outcome;
        try
        {
            outcome = Normalize(await RunClientAsync(item).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            TryLog(() => log.Error($"İşlem istisna ile bitti: {item.Kind} {package.Id}", ex));
            outcome = new OperationOutcome(OperationStatus.Failed, ex.Message);
        }

        item.Complete(outcome);
        lock (gate)
        {
            unfinished.Remove(item);
            CountOutcome(outcome.Status);
        }

        TryLog(() => log.Info($"İşlem bitti: {item.Kind} {package.Id} -> {outcome.Status} ({outcome.Message})"));
    }

    private Task<OperationOutcome> RunClientAsync(OperationItem item)
    {
        var progress = new ItemProgress(item);
        var id = item.Package.Id;
        return item.Kind switch
        {
            OperationKind.Install => client.InstallAsync(id, progress, CancellationToken.None),
            OperationKind.Upgrade => client.UpgradeAsync(id, progress, CancellationToken.None),
            OperationKind.Uninstall => client.UninstallAsync(id, progress, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Kind, "Bilinmeyen işlem türü."),
        };
    }

    /// <summary>İstemci Queued/Running gibi bitmemiş bir durum döndürürse işlem sonsuza dek "çalışıyor" kalmasın.</summary>
    private static OperationOutcome Normalize(OperationOutcome outcome) =>
        outcome.Status is OperationStatus.Queued or OperationStatus.Running
            ? new OperationOutcome(OperationStatus.Failed, $"İstemci geçersiz bir durum döndürdü: {outcome.Status}")
            : outcome;

    private void CountOutcome(OperationStatus status)
    {
        switch (status)
        {
            case OperationStatus.Succeeded: succeeded++; break;
            case OperationStatus.Cancelled: cancelled++; break;
            case OperationStatus.NotFound: notFound++; break;
            default: failed++; break;
        }
    }

    /// <returns>Döngü bitmeliyse <c>true</c>; olay sırasında yeni iş eklendiyse <c>false</c>.</returns>
    private async Task<bool> TryFinishBatchAsync()
    {
        BatchSummary summary;
        lock (gate)
        {
            summary = new BatchSummary(succeeded, failed, cancelled, notFound);
            succeeded = failed = cancelled = notFound = 0;
        }

        await ContextMarshal.PostAsync(context, () => RaiseBatchCompleted(summary)).ConfigureAwait(false);

        lock (gate)
        {
            if (pending.Count > 0)
            {
                return false;
            }

            loopRunning = false;
            idle.SetResult();
            NotifyIsBusyChanged();
            return true;
        }
    }

    private void RaiseBatchCompleted(BatchSummary summary)
    {
        TryLog(() => log.Info(
            $"Toplu iş bitti: {summary.Succeeded} başarılı, {summary.Failed} başarısız, " +
            $"{summary.Cancelled} iptal, {summary.NotFound} bulunamadı"));
        try
        {
            BatchCompleted?.Invoke(this, summary);
        }
        catch (Exception ex)
        {
            // Dinleyicinin hatası döngüyü öldürmemeli; yutulmaz, günlüğe yazılır.
            TryLog(() => log.Error("BatchCompleted dinleyicisi istisna fırlattı", ex));
        }
    }

    /// <summary>Çağıran gate kilidini tutarken çağırır; bildirim sırası durum değişikliği sırasıyla aynı kalır.</summary>
    private void NotifyIsBusyChanged() => ContextMarshal.Post(context, () => OnPropertyChanged(nameof(IsBusy)));

    /// <summary>Günlük yazılamaması (disk dolu vb.) kuyruk döngüsünü öldürmesin; hata en azından izleme çıktısına gider.</summary>
    private static void TryLog(Action write)
    {
        try
        {
            write();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"KolayKur günlüğüne yazılamadı: {ex}");
        }
    }

    private sealed class ItemProgress(OperationItem item) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => item.ReportProgress(value);
    }
}
