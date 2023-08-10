using MessagePack;
using NSec.Cryptography;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Genesis : Transaction
{
    public Genesis()
    {

    }
}
