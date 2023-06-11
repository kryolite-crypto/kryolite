using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

[MessagePackObject]
public class Genesis : Transaction
{
    [IgnoreMember]
    public string NetworkName { get; set; } = string.Empty;

    public Genesis()
    {

    }

    public Genesis(Transaction tx) 
    {
        NetworkName = Encoding.UTF8.GetString(tx.Data ?? new byte[0]);
    }
}
