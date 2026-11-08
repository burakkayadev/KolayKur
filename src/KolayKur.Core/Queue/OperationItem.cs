using CommunityToolkit.Mvvm.ComponentModel;
using KolayKur.Core.Localization;
using KolayKur.Core.Models;

namespace KolayKur.Core.Queue;

/// <summary>
/// Kuyruktaki tek bir işlem. Özellikler yalnızca kuyruk tarafından, kuyruğun kurucuda yakaladığı
/// <see cref="SynchronizationContext"/> üzerinde değiştirilir; arayüz bağlamaları güvenle dinleyebilir.
/// <see cref="Message"/> çalışırken geçerli <see cref="ProgressStage"/> anahtarı, bittiğinde sonucun dilden bağımsız
/// teknik ayrıntısıdır. Kullanıcıya gösterilen metin <see cref="DetailText"/>'tir (geçerli dilde, dil değişince yenilenir).
/// </summary>
public sealed class OperationItem : ObservableObject
{
    private readonly SynchronizationContext? context;
    private OperationStatus status = OperationStatus.Queued;
    private double? percent;
    private string? message;
    private string? reasonKey;

    internal OperationItem(OperationKind kind, PackageInfo package, SynchronizationContext? context)
    {
        Kind = kind;
        Package = package;
        this.context = context;
        Loc.Instance.WhenLanguageChanges(this, static self => self.OnPropertyChanged(nameof(DetailText)));
    }

    public OperationKind Kind { get; }

    public PackageInfo Package { get; }

    public OperationStatus Status
    {
        get => status;
        private set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(TechnicalDetail));
                OnPropertyChanged(nameof(ReasonKey));
            }
        }
    }

    public double? Percent
    {
        get => percent;
        private set => SetProperty(ref percent, value);
    }

    public string? Message
    {
        get => message;
        private set
        {
            if (SetProperty(ref message, value))
            {
                OnPropertyChanged(nameof(DetailText));
                OnPropertyChanged(nameof(TechnicalDetail));
            }
        }
    }

    /// <summary>Bitmiş işin neden metni anahtarı (<see cref="OperationReason"/>); başarılı veya bitmemiş işte <c>null</c>.</summary>
    public string? ReasonKey => OperationTexts.ReasonKeyFor(status, reasonKey);

    /// <summary>Listede gösterilen ayrıntı, geçerli dilde: çalışırken aşama, bittiğinde yerelleştirilmiş neden.</summary>
    public string DetailText => OperationTexts.DetailText(status, message, reasonKey);

    /// <summary>Bitmiş işin teknik ayrıntısı (hata kodları); yalnızca ipucu ve günlük içindir. Bitmemişse <c>null</c>.</summary>
    public string? TechnicalDetail =>
        status is OperationStatus.Queued or OperationStatus.Running ? null : message;

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
        reasonKey = outcome.ReasonKey;
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
