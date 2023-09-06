using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared;

public static class Base32
{
    public static SimpleBase.Base32 Kryolite = new SimpleBase.Base32(new SimpleBase.Base32Alphabet("abcdefghijkmnpqrstuvwxyz23456789"));
}
