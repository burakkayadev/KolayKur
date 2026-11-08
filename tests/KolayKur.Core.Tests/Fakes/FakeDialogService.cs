using KolayKur.Core.Support;

namespace KolayKur.Core.Tests.Fakes;

public sealed class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; }

    public List<string> Confirmed { get; } = [];

    public List<string> Informed { get; } = [];

    public bool Confirm(string message)
    {
        Confirmed.Add(message);
        return ConfirmResult;
    }

    public void Inform(string message) => Informed.Add(message);

    /// <summary>Dosya seçicinin döndüreceği yol; <c>null</c> kullanıcının vazgeçtiği anlamına gelir.</summary>
    public string? OpenFilePath { get; set; }

    public string? SaveFilePath { get; set; }

    /// <summary>Doluysa <see cref="OpenFile"/> bunu fırlatır.</summary>
    public Exception? OpenFileException { get; set; }

    public List<string> Opened { get; } = [];

    public string? PickOpenFile() => OpenFilePath;

    public string? PickSaveFile() => SaveFilePath;

    public void OpenFile(string path)
    {
        Opened.Add(path);
        if (OpenFileException is not null)
        {
            throw OpenFileException;
        }
    }
}
