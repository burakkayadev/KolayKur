using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KolayKur.App.Views;

public partial class ErrorBanner : UserControl
{
    public static readonly DependencyProperty ErrorKeyProperty =
        DependencyProperty.Register(nameof(ErrorKey), typeof(string), typeof(ErrorBanner));

    public static readonly DependencyProperty RetryCommandProperty =
        DependencyProperty.Register(nameof(RetryCommand), typeof(ICommand), typeof(ErrorBanner));

    public ErrorBanner()
    {
        InitializeComponent();
    }

    /// <summary>Gösterilecek hata metninin <c>Strings.resx</c> anahtarı; <c>null</c> ise banner gizlenir.</summary>
    public string? ErrorKey
    {
        get => (string?)GetValue(ErrorKeyProperty);
        set => SetValue(ErrorKeyProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => (ICommand?)GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }
}
