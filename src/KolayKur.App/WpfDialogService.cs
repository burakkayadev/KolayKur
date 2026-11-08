using System.Diagnostics;
using System.Windows;
using KolayKur.Core.Localization;
using KolayKur.Core.Support;
using Microsoft.Win32;

namespace KolayKur.App;

public sealed class WpfDialogService : IDialogService
{
    private const string ListFileFilterKey = "Dialog_ListFilter";

    public bool Confirm(string message) =>
        MessageBox.Show(message, Loc.Instance["App_Title"], MessageBoxButton.YesNo, MessageBoxImage.Question)
        == MessageBoxResult.Yes;

    public void Inform(string message) =>
        MessageBox.Show(message, Loc.Instance["App_Title"], MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PickOpenFile()
    {
        var dialog = new OpenFileDialog { Filter = Loc.Instance[ListFileFilterKey] };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile()
    {
        var dialog = new SaveFileDialog { Filter = Loc.Instance[ListFileFilterKey], DefaultExt = ".json" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void OpenFile(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
