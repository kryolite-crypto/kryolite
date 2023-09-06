using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Node;

public static class Compressor
{
    public static byte[] Compress(this Span<byte> data)
    {
        using var output = new MemoryStream();
        using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
        {
            dstream.Write(data);
        }

        return output.ToArray();
    }

    public static ReadOnlySpan<byte> Decompress(this ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var output = new MemoryStream();
        using (var dstream = new DeflateStream(input, CompressionMode.Decompress))
        {
            dstream.CopyTo(output);
        }

        return output.ToArray();
    }
}
