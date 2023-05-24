using Kryolite.Shared.Dto;
using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

public class Payment : Transaction
{
    public new PublicKey PublicKey { get; set; }
    public new Signature Signature { get; set; }

    public Payment()
    {
        PublicKey = new PublicKey();
        Signature = new Signature();
    }
}
