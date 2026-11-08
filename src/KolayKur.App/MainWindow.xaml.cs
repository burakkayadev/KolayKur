using System.ComponentModel;
using System.Windows;
using KolayKur.Core.ViewModels;

namespace KolayKur.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel shell;

    public MainWindow(ShellViewModel shell)
    {
        this.shell = shell;
        DataContext = shell;
        InitializeComponent();
    }

    // İşlem sürerken kapatmak onay ister; kullanıcı vazgeçerse pencere açık kalır.
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!shell.ConfirmClose())
        {
            e.Cancel = true;
        }
    }
}
