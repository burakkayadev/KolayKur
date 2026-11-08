using System.Windows;
using System.Windows.Controls;
using KolayKur.Core.ViewModels;

namespace KolayKur.App.Views;

public partial class UpdatesPage : UserControl
{
    public UpdatesPage()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    // Sayfa her görünür olduğunda liste tazelenir (kabuk denetimi bitince ve menüden dönüldüğünde).
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is UpdatesViewModel viewModel)
        {
            viewModel.LoadCommand.Execute(null);
        }
    }
}
