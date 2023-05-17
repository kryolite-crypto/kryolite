using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

public class TransactionJoin
{
    public SHA256Hash ValidatesId { get; set; } = new SHA256Hash();
    public SHA256Hash ValidatedById { get; set; } = new SHA256Hash();
}
