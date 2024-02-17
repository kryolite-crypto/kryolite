using System.Runtime.InteropServices;
using System.Text;

namespace Kryolite.RocksDb;

public class DbPath : IDisposable
{
    public nint Handle => _handle;

    private nint _handle;

    public DbPath(string path)
    {
        byte[] bytes = [..Encoding.UTF8.GetBytes(path), 0];
        _handle = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, _handle, bytes.Length);
    }

    public void Dispose()
    {
        Marshal.FreeHGlobal(_handle);
    }
}
