using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Executor;

public interface IExecutorFactory
{
    Executor Create(IExecutorContext context);
}
