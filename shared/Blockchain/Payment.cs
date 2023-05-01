using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

public class Payment : Transaction
{
    public new PublicKey PublicKey { get; set; } = new PublicKey();
    public new Signature Signature { get; set; } = new Signature();
}
