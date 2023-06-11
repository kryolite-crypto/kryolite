using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node.Services;

public interface IBufferService<T, Y> : IHostedService
{
    void Add(T item);
    Task AddAsync(T item);
    void Add(List<T> items);
    Task AddAsync(List<T> items);
}
