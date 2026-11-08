using CommunityToolkit.Mvvm.ComponentModel;
using KolayKur.Core.Models;

namespace KolayKur.Core.Queue;

/// <summary>
/// Kuyruktaki tek bir işlem. Özellikler yalnızca kuyruk tarafından, kuyruğun kurucuda yakaladığı
/// <see cref="SynchronizationContext"/> üzerinde değiştirilir; arayüz bağlamaları güvenle dinleyebilir.
/// <see cref="Message"/> çalışırken geçerli <see cref="ProgressStage"/> anahtarı, bittiğinde sonucun teknik ayrıntısıdır.
/// </summary>
public sealed class OperationItem : ObservableObject
{
    private readonly SynchronizationContext? context;
    private OperationStatus status = OperationStatus.Queued;
    private double? percent;
    private string? message;

    internal OperationItem(OperationKind kind, PackageInfo package, SynchronizationContext? context)
    {
        Kind = kind;
        Package = package;
        this.context = context;
    }

    public OperationKind Kind { get; }

    public PackageInfo Package { get; }

    public OperationStatus Status
    {
        get => status;
        private set => SetProperty(ref status, value);
    }

    public double? Percent
    {
        get => percent;
        private set => SetProperty(ref percent, value);
    }

    public string? Message
    {
        get => message;
        private set => SetProperty(ref message, value);
    }

    internal void MarkRunning() => ContextMarshal.Post(context, () => Status = OperationStatus.Running);

    /// <summary>Geç gelen bildirimler (iş bittikten sonra) yok sayılır.</summary>
    internal void ReportProgress(OperationProgress progress) => ContextMarshal.Post(context, () =>
    {
        if (Status != OperationStatus.Running)
        {
            return;
        }

        Percent = progress.Percent;
        Message = progress.Stage;
    });

    internal void Complete(OperationOutcome outcome) => ContextMarshal.Post(context, () =>
    {
        Message = outcome.Message;
        Status = outcome.Status;
    });
}

/// <summary>Değişiklikleri yakalanan bağlama taşır; bağlam yoksa çağıran iş parçacığında doğrudan uygular.</summary>
internal static class ContextMarshal
{
    public static void Post(SynchronizationContext? context, Action action)
    {
        if (context is null)
        {
            action();
            return;
        }

        context.Post(static state => ((Action)state!)(), action);
    }

    public static Task PostAsync(SynchronizationContext? context, Action action)
    {
        if (context is null)
        {
            action();
            return Task.CompletedTask;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            _ =>
            {
                try
                {
                    action();
                }
                finally
                {
                    done.TrySetResult();
                }
            },
            null);
        return done.Task;
    }
}
