using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using static System.Net.Mime.MediaTypeNames;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Kryolite.Shared;

public static class BWT
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Encode(ReadOnlySpan<byte> input, Span<byte> output, out int index)
    {
        var newInput = new int[input.Length + 1];

        for (int i = 0; i < input.Length; i++)
        {
            newInput[i] = input[i] + 1;
        }

        newInput[input.Length] = 0;

        var sortedSuffixes = SuffixArray.BuildSuffixArray(newInput, 257)[1..];

        index = 0;
        var outputInd = 0;

        for (var i = 0; i < sortedSuffixes.Length; i++)
        {
            int idx = sortedSuffixes[i];

            if (idx == 0)
            {
                index = i;
                continue;
            }

            output[outputInd] = (byte)(newInput[sortedSuffixes[i] - 1] - 1);
            outputInd++;
        }
    }

    public static byte[] Decode(byte[] input, int index)
    {
        var length = input.Length;
        var freq = new int[256];

        // T1: Number of Preceding Symbols Matching Symbol in Current Position.
        var t1 = new int[length + 1];
        // T2: Number of Symbols Lexicographically Less Than Current Symbol
        var t2 = new int[256];

        // Construct T1
        for (var i = 0; i < length; i++)
        {
            t1[i] = freq[input[i]];
            freq[input[i]]++;
        }

        // Construct T2
        // Add $ special symbol in consideration to be less than any symbol
        t2[0] = 1;
        for (var i = 1; i < 256; i++)
        {
            t2[i] = t2[i - 1] + freq[i - 1];
        }

        var output = new byte[length];
        var nxt = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            output[i] = input[nxt];
            var a = t1[nxt];
            var b = t2[input[nxt]];
            nxt = a + b;
            // Add $ special symbol index in consideration
            if (nxt >= index)
            {
                nxt--;
            }
        }
        return output;
    }
}