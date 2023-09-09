using Avalonia.Media;
using Kryolite.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public class TokenModel : NotifyPropertyChanged
{
    private SHA256Hash tokenId = SHA256Hash.NULL_HASH;
    private Address owner = Address.NULL_ADDRESS;
    private string name = string.Empty;
    private string description = string.Empty;
    private bool isConsumed;
    private IBrush foregroundColor = Brushes.White;

    public SHA256Hash TokenId 
    {
        get => tokenId;
        set => RaisePropertyChanged(ref tokenId, value);
    }

    public Address Owner 
    {
        get => owner;
        set => RaisePropertyChanged(ref owner, value);
    }


    public string Name 
    {
        get => name;
        set => RaisePropertyChanged(ref name, value);
    }


    public string Description 
    {
        get => description;
        set => RaisePropertyChanged(ref description, value);
    }


    public bool IsConsumed 
    {
        get => isConsumed;
        set
        {
            RaisePropertyChanged(ref isConsumed, value);
            ForegroundColor = IsConsumed ? Brush.Parse("#66FFFFFF") : Brushes.White;
        }
    }


    public IBrush ForegroundColor
    {
        get => foregroundColor;
        set => RaisePropertyChanged(ref foregroundColor, value);
    }
}
