using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KolayKur.App.Views;

public partial class ErrorInfoBar : UserControl
{
    public static readonly DependencyProperty ErrorKeyProperty =
        DependencyProperty.Register(nameof(ErrorKey), typeof(string), typeof(ErrorInfoBar));

    public static readonly DependencyProperty ShowRetryProperty =
        DependencyProperty.Register(nameof(ShowRetry), typeof(bool), typeof(ErrorInfoBar));

    public static readonly DependencyProperty RetryCommandProperty =
        DependencyProperty.Register(nameof(RetryCommand), typeof(ICommand), typeof(ErrorInfoBar));

    public ErrorInfoBar()
    {
        InitializeComponent();
    }

    /// <summary>Gösterilecek hata metninin <c>Strings.resx</c> anahtarı; <c>null</c> ise çubuk gizlenir.</summary>
    public string? ErrorKey
    {
        get => (string?)GetValue(ErrorKeyProperty);
        set => SetValue(ErrorKeyProperty, value);
    }

    /// <summary>"Tekrar dene" düğmesi görünsün mü (yalnız yeniden denenebilir hatalarda).</summary>
    public bool ShowRetry
    {
        get => (bool)GetValue(ShowRetryProperty);
        set => SetValue(ShowRetryProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }
}
