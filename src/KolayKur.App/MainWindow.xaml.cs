using System.ComponentModel;
using KolayKur.Core.ViewModels;
using Wpf.Ui.Controls;

namespace KolayKur.App;

public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel shell;

    public MainWindow(
        ShellViewModel shell,
        SearchViewModel search,
        UpdatesViewModel updates,
        InstalledViewModel installed,
        SettingsViewModel settings,
        OperationListViewModel operations)
    {
        this.shell = shell;
        DataContext = shell;
        InitializeComponent();

        // Sayfaların kendi ViewModel'i vardır; kabuk yalnızca hangisinin görüneceğini seçer.
        searchPage.DataContext = search;
        updatesPage.DataContext = updates;
        updatesBadge.DataContext = updates;
        installedPage.DataContext = installed;
        settingsPage.DataContext = settings;
        operationsPane.DataContext = operations;
        operationsToggle.DataContext = operations;
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
