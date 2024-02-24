using System.Runtime.InteropServices;

namespace Kryolite.RocksDb;

public class RocksDbException : Exception
{
    public override string Message => _message;

    private string _message;

    public RocksDbException(nint errorptr)
    {
        _message = Marshal.PtrToStringAnsi(errorptr) ?? string.Empty;
        Interop.rocksdb_free(errorptr);
    }
}
