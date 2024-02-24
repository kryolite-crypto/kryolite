using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared;

public static class Base32
{
    public static readonly SimpleBase.Base32 Kryolite = new(new SimpleBase.Base32Alphabet("abcdefghijkmnpqrstuvwxyz23456789"));
}
