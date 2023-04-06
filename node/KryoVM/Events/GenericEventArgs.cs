using Kryolite.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class GenericEventArgs : EventArgs
{
    public string Json { get; set; } = string.Empty;
}
