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
    private SHA256Hash _tokenId;
    public SHA256Hash TokenId 
    {
        get => _tokenId;
        set
        {
            if (_tokenId != value)
            {
                _tokenId = value;
                RaisePropertyChanged();
            }
        }
    }

    private Address _owner;
    public Address Owner 
    {
        get => _owner;
        set
        {
            if (_owner != value)
            {
                _owner = value;
                RaisePropertyChanged();
            }
        }
    }

    private string _name;
    public string Name 
    {
        get => _name;
        set
        {
            if (value != _name) 
            {
                _name = value;
                RaisePropertyChanged();
            }
        }
    }

    private string _description;
    public string Description 
    {
        get => _description;
        set
        {
            if (value != _description)
            {
                _description = value;
                RaisePropertyChanged();
            }
        }
    }

    private bool _isConsumed;
    public bool IsConsumed 
    {
        get => _isConsumed;
        set
        {
            if (_isConsumed != value)
            {
                _isConsumed = value;
                RaisePropertyChanged();
            }

            ForegroundColor = IsConsumed ? Brush.Parse("#66FFFFFF") : Brushes.White;
        }
    }

    private IBrush _foregroundColor = Brushes.White;
    public IBrush ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            if (_foregroundColor != value)
            {
                _foregroundColor = value;
                RaisePropertyChanged();
            }
        }
    }
}
