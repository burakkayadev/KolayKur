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

    public string? PickOpenFile() => null;

    public string? PickSaveFile() => null;

    public void OpenFile(string path)
    {
    }
}
