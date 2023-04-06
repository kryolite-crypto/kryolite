using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Wallet;

public class TokenModel : NotifyPropertyChanged
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsConsumed { get; set; }
}
