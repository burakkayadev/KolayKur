using System.Collections.Concurrent;

namespace KolayKur.Core.Tests.Fakes;

/// <summary>Tek özel iş parçacığında geri çağrıları sırayla çalıştıran, arayüz iş parçacığını taklit eden bağlam.</summary>
internal sealed class PumpContext : SynchronizationContext, IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> work = [];
    private readonly Thread thread;

    public PumpContext()
    {
        thread = new Thread(Pump) { IsBackground = true };
        thread.Start();
    }

    public int ThreadId => thread.ManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state) => work.Add((d, state));

    /// <summary>İşi pompa iş parçacığında çalıştırır (kuyruk bağlamı yakalasın diye).</summary>
    public T Run<T>(Func<T> func)
    {
        T result = default!;
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        Post(_ =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        }, null);
        Assert.True(done.Wait(WaitTimeout), "Pump did not run the work in time.");
        return failure is null ? result : throw new InvalidOperationException("Pump work failed.", failure);
    }

    public Task FlushAsync()
    {
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => flushed.TrySetResult(), null);
        return flushed.Task.WaitAsync(WaitTimeout);
    }

    public void Dispose() => work.CompleteAdding();

    private void Pump()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in work.GetConsumingEnumerable())
        {
            callback(state);
        }
    }
}
