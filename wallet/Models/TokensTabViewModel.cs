using System.Collections.ObjectModel;
using System.Linq;

namespace Kryolite.Wallet;

public class TokensTabViewModel : NotifyPropertyChanged
{
    private ObservableCollection<TokenModel> tokens = new ObservableCollection<TokenModel>();

    public ObservableCollection<TokenModel> Tokens
    {
        get { return tokens; }
        set
        {
            if (!Enumerable.SequenceEqual(tokens, value))
            {
                tokens = value;
                RaisePropertyChanged();
            }
        }
    }
}
