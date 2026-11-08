using System.ComponentModel;
using System.Windows;
using KolayKur.Core.ViewModels;

namespace KolayKur.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel shell;

    public MainWindow(
        ShellViewModel shell,
        SearchViewModel search,
        UpdatesViewModel updates,
        OperationListViewModel operations)
    {
        this.shell = shell;
        DataContext = shell;
        InitializeComponent();

        // Sayfaların kendi ViewModel'i vardır; kabuk yalnızca hangisinin görüneceğini seçer.
        searchPage.DataContext = search;
        updatesPage.DataContext = updates;
        operationList.DataContext = operations;
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
