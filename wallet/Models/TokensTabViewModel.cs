using System.Collections.ObjectModel;
using System.Linq;

namespace Kryolite.Wallet;

public class TokensTabViewModel : NotifyPropertyChanged
{
    private ObservableCollection<TokenModel> _Tokens = new ObservableCollection<TokenModel>();
    public ObservableCollection<TokenModel> Tokens
    {
        get { return _Tokens; }
        set
        {
            if (!Enumerable.SequenceEqual(_Tokens, value))
            {
                _Tokens = value;
                RaisePropertyChanged();
            }
        }
    }
}
