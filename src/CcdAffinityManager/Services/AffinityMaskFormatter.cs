using System.Numerics;
using System.Text;

namespace CcdAffinityManager.Services;

public static class AffinityMaskFormatter
{
    public static string Format(ulong mask)
    {
        if (mask == 0)
        {
            return "未选择";
        }

        return $"CPU {FormatRanges(mask)} (0x{mask:X})";
    }

    public static string FormatRanges(ulong mask)
    {
        if (mask == 0)
        {
            return "无";
        }

        var builder = new StringBuilder();
        var bitIndex = 0;

        while (bitIndex < 64)
        {
            while (bitIndex < 64 && (mask & (1UL << bitIndex)) == 0)
            {
                bitIndex++;
            }

            if (bitIndex >= 64)
            {
                break;
            }

            var start = bitIndex;
            while (bitIndex + 1 < 64 && (mask & (1UL << (bitIndex + 1))) != 0)
            {
                bitIndex++;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(start);
            if (bitIndex > start)
            {
                builder.Append('-').Append(bitIndex);
            }

            bitIndex++;
        }

        return builder.ToString();
    }

    public static int CountProcessors(ulong mask)
    {
        return BitOperations.PopCount(mask);
    }
}
