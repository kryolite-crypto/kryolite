using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared;

public static class SuffixArray
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int[] BuildSuffixArray(int[] data, int alphabetSize)
    {
        var typemap = BuildTypeMap(data);
        var bucketSizes = FindBucketSizes(data, alphabetSize);

        var sa = GenerateSA(data, bucketSizes, typemap);

        InduceSortL(data, sa, bucketSizes, typemap);
        InduceSortS(data, sa, bucketSizes, typemap);

        var (summaryData, summarySuffixOffsets, summaryAlphabetSize) = SummarizeSuffixArray(data, sa, typemap);

        var summarySA = MakeSummarySuffixArray(summaryData, summaryAlphabetSize);

        var result = AccurateLMSSort(data, bucketSizes, summarySA, summarySuffixOffsets);

        InduceSortL(data, result, bucketSizes, typemap);
        InduceSortS(data, result, bucketSizes, typemap);

        return result;
    }

    private static readonly byte S_TYPE = (byte)'S';
    private static readonly byte L_TYPE = (byte)'L';

    private static byte[] BuildTypeMap(int[] data)
    {
        var res = new byte[data.Length + 1];

        res[data.Length] = S_TYPE;

        if (data.Length == 0)
        {
            return res;
        }

        res[data.Length - 1] = L_TYPE;

        for (var i = data.Length - 2; i >= 0; i--)
        {
            if (data[i] > data[i + 1])
            {
                res[i] = L_TYPE;
            }
            else if (data[i] == data[i + 1] && res[i + 1] == L_TYPE)
            {
                res[i] = L_TYPE;
            }
            else
            {
                res[i] = S_TYPE;
            }
        }

        return res;
    }

    private static bool IsLMS(int offset, byte[] typemap)
    {
        if (offset == 0)
        {
            return false;
        }

        if (typemap[offset] == S_TYPE && typemap[offset - 1] == L_TYPE)
        {
            return true;
        }

        return false;
    }

    private static bool LMSSubstringEquals(int[] data, byte[] typemap, int offsetA, int offsetB)
    {
        if (offsetA == data.Length || offsetB == data.Length)
        {
            return false;
        }

        int i = 0;

        while (true)
        {
            var a = IsLMS(i + offsetA, typemap);
            var b = IsLMS(i + offsetB, typemap);

            if (i > 0 && a && b)
            {
                return true;
            }

            if (a != b)
            {
                return false;
            }

            if (data[i + offsetA] != data[i + offsetB])
            {
                return false;
            }

            i++;
        }
    }

    private static int[] FindBucketSizes(int[] data, int alphabetSize)
    {
        var res = new int[alphabetSize];

        for (var i = 0; i < data.Length; i++)
        {
            res[data[i]] += 1;
        }

        return res;
    }

    private static int[] FindBucketHeads(int[] bucketSizes)
    {
        var res = new int[bucketSizes.Length];
        var offset = 1;
        var items = 0;

        for (var i = 0; i < bucketSizes.Length; i++)
        {
            res[items++] = offset;
            offset += bucketSizes[i];
        }

        return res;
    }

    private static int[] FindBucketTails(int[] bucketSizes)
    {
        var res = new int[bucketSizes.Length];
        var offset = 1;
        var items = 0;

        for (var i = 0; i < bucketSizes.Length; i++)
        {
            offset += bucketSizes[i];
            res[items++] = offset - 1;
        }

        return res;
    }

    private static int[] GenerateSA(int[] data, int[] bucketSizes, byte[] typemap)
    {
        var sa = new int[data.Length + 1];
        Array.Fill(sa, -1);

        var bucketTails = FindBucketTails(bucketSizes);

        for (var i = 0; i < data.Length; i++)
        {
            if (!IsLMS(i, typemap))
            {
                continue;
            }

            var bucketIndex = data[i];

            sa[bucketTails[bucketIndex]] = i;

            bucketTails[bucketIndex] -= 1;
        }

        sa[0] = data.Length;

        return sa;
    }

    public static void InduceSortL(int[] data, int[] sa, int[] bucketSizes, byte[] typemap)
    {
        var bucketHeads = FindBucketHeads(bucketSizes);

        for (var i = 0; i < sa.Length; i++)
        {
            if (sa[i] == -1)
            {
                continue;
            }

            var j = sa[i] - 1;

            if (j < 0)
            {
                continue;
            }

            if (typemap[j] != L_TYPE)
            {
                continue;
            }

            var bucketIndex = data[j];

            sa[bucketHeads[bucketIndex]] = j;

            bucketHeads[bucketIndex] += 1;
        }
    }

    public static void InduceSortS(int[] data, int[] sa, int[] bucketSizes, byte[] typemap)
    {
        var bucketTails = FindBucketTails(bucketSizes);

        for (var i = sa.Length - 1; i >= 0; i--)
        {
            var j = sa[i] - 1;

            if (j < 0)
            {
                continue;
            }

            if (typemap[j] != S_TYPE)
            {
                continue;
            }

            var bucketIndex = data[j];

            sa[bucketTails[bucketIndex]] = j;

            bucketTails[bucketIndex] -= 1;
        }
    }

    public static (int[], int[], int) SummarizeSuffixArray(int[] data, int[] sa, byte[] typemap)
    {
        var lms = new int[data.Length + 1];

        Array.Fill(lms, -1);

        var current = 0;

        lms[sa[0]] = current;
        var lastLMSSuffixOffset = sa[0];

        for (var i = 1; i < sa.Length; i++)
        {
            var suffixOffset = sa[i];

            if (!IsLMS(suffixOffset, typemap))
            {
                continue;
            }

            if (!LMSSubstringEquals(data, typemap, lastLMSSuffixOffset, suffixOffset))
            {
                current++;
            }

            lastLMSSuffixOffset = suffixOffset;
            lms[suffixOffset] = current;
        }

        var summarySuffixOffsets = new Span<int>(new int[data.Length]);
        var summaryData = new Span<int>(new int[data.Length]);

        var items = 0;

        for (var i = 0; i < lms.Length; i++)
        {
            if (lms[i] == -1)
            {
                continue;
            }

            summarySuffixOffsets[items] = i;
            summaryData[items] = lms[i];

            items++;
        }

        var summaryAlphabetSize = current + 1;

        return (summaryData[0..items].ToArray(), summarySuffixOffsets[0..items].ToArray(), summaryAlphabetSize);
    }

    private static int[] AccurateLMSSort(int[] data, int[] bucketSizes, int[] summarySuffixArray, int[] summarySuffixOffsets)
    {
        var suffixOffsets = new int[data.Length + 1];

        Array.Fill(suffixOffsets, -1);

        var bucketTails = FindBucketTails(bucketSizes);

        for (var i = summarySuffixArray.Length - 1; i > 1; i--)
        {
            var index = summarySuffixOffsets[summarySuffixArray[i]];

            var bucketIndex = data[index];

            suffixOffsets[bucketTails[bucketIndex]] = index;
            bucketTails[bucketIndex] -= 1;
        }

        suffixOffsets[0] = data.Length;

        return suffixOffsets;
    }

    public static int[] MakeSummarySuffixArray(int[] summaryData, int summaryAlphabetSize)
    {
        if (summaryData.Length == summaryAlphabetSize)
        {
            var summarySuffixArray = new int[summaryData.Length + 1];
            Array.Fill(summarySuffixArray, -1);

            summarySuffixArray[0] = summaryData.Length;

            for (int x = 0; x < summaryData.Length; x++)
            {
                var y = summaryData[x];
                summarySuffixArray[y + 1] = x;
            }

            return summarySuffixArray;
        }
        else
        {
            return BuildSuffixArray(summaryData, summaryAlphabetSize);
        }
    }
}
