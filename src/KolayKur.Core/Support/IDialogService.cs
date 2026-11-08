namespace KolayKur.Core.Support;

/// <summary>Kullanıcıyla konuşan pencereler; ViewModel'ler WPF'e bağlanmasın diye arayüz olarak verilir.</summary>
public interface IDialogService
{
    bool Confirm(string message);

    void Inform(string message);

    string? PickOpenFile();

    string? PickSaveFile();

    void OpenFile(string path);
}
