using Kryolite.Shared;
using Redbus.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class GenericEventArgs : EventBase
{
    public string Json { get; set; } = string.Empty;
}
