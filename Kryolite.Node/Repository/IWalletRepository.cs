using Kryolite.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Repository;

public interface IWalletRepository
{
    void Add(Wallet wallet);
    Wallet? Get(Address address);
    void UpdateDescription(Address address, string description);
    Dictionary<Address, Wallet> GetWallets();
}
