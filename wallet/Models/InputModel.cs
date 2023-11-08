namespace Kryolite.Wallet;

public class InputModel : NotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _input = string.Empty;

    public string Title
    {
        get => _title;
        set => RaisePropertyChanged(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => RaisePropertyChanged(ref _description, value);
    }

    public string Input
    {
        get => _input;
        set => RaisePropertyChanged(ref _input, value);
    }
}
