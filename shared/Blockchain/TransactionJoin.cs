using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

public class TransactionJoin
{
    public SHA256Hash ParentId { get; set; } = new SHA256Hash();
    public SHA256Hash ChildId { get; set; } = new SHA256Hash();
}
