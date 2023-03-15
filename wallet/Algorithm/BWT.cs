using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared;

public static class Bwt
{
    /// <summary>
    /// Transforms input bytes using Bwt
    /// </summary>
    /// <param name="input">Type byte[], should return transformed byte[]</param>
    /// <returns></returns>
    public static async Task<byte[]> Transform(byte[] input)
    {
        var task = Task.Factory.StartNew(() => {
            Console.WriteLine("Performing BWT");
            var output = new byte[input.Length + 4];
            var newInput = new short[input.Length + 1];

            for (var i = 0; i < input.Length; i++)
                newInput[i] = (short)(input[i] + 1);

            newInput[input.Length] = 0;
            var suffixArray = SuffixArray.Construct(newInput);
            var end = 0;
            var outputInd = 0;
            for (var i = 0; i < suffixArray.Length; i++)
            {
                if (suffixArray[i] == 0)
                {
                    end = i;
                    continue;
                }

                output[outputInd] = (byte)(newInput[suffixArray[i] - 1] - 1);
                outputInd++;
            }

            var endByte = IntToByteArr(end);
            endByte.CopyTo(output, input.Length);
            Console.WriteLine("BWT Ended");
            return output;
        });

        return await task;
    }
    /// <summary>
    /// transforms bwt to initial byte array
    /// </summary>
    /// <param name="input">Type byte[] should return inverse transformed byte[]</param>
    /// <returns></returns>
    public static async Task<byte[]> InverseTransform(byte[] input)
    {
        var task = Task.Factory.StartNew(() => {
            var length = input.Length - 4;
            var I = ByteArrToInt(input, input.Length - 4);
            var freq = new int[256];
            Array.Clear(freq, 0, freq.Length);
            // T1: Number of Preceding Symbols Matching Symbol in Current Position.
            var t1 = new int[length];
            // T2: Number of Symbols Lexicographically Less Than Current Symbol
            var t2 = new int[256];
            Array.Clear(t2, 0, t2.Length);
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
                if (nxt >= I)
                {
                    nxt--;
                }
            }
            return output;
        });
        return await task;
    }
    private static byte[] IntToByteArr(int i)
    {
        return BitConverter.GetBytes(i);
    }
    private static int ByteArrToInt(byte[] input, int startIndex)
    {
        return BitConverter.ToInt32(input, startIndex);
    }
}
