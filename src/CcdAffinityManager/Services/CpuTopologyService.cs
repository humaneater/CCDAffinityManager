using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using CcdAffinityManager.Native;

namespace CcdAffinityManager.Services;

public sealed record CpuTopology(
    ulong SystemMask,
    ulong RecommendedMask,
    uint RecommendedCacheSizeBytes,
    int LogicalProcessorCount,
    int ProcessorGroupCount)
{
    public string RecommendedDescription
    {
        get
        {
            var cacheText = RecommendedCacheSizeBytes > 0
                ? $"，L3 {FormatCacheSize(RecommendedCacheSizeBytes)}"
                : string.Empty;

            return $"大缓存 CCD：CPU {AffinityMaskFormatter.FormatRanges(RecommendedMask)}" +
                   $"（{AffinityMaskFormatter.CountProcessors(RecommendedMask)} 个逻辑处理器{cacheText}）";
        }
    }

    public string ToDiagnosticText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("CcdAffinityManager topology diagnostics");
        builder.AppendLine($"Processor groups: {ProcessorGroupCount}");
        builder.AppendLine($"Logical processors: {LogicalProcessorCount}");
        builder.AppendLine($"System mask: 0x{SystemMask:X}");
        builder.AppendLine($"Recommended mask: 0x{RecommendedMask:X}");
        builder.AppendLine($"Recommended CPUs: {AffinityMaskFormatter.FormatRanges(RecommendedMask)}");
        builder.AppendLine(
            $"Recommended cache: {FormatCacheSize(RecommendedCacheSizeBytes)}");
        return builder.ToString();
    }

    private static string FormatCacheSize(uint bytes)
    {
        if (bytes == 0)
        {
            return "unknown";
        }

        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024d):0.#} MB";
        }

        return $"{bytes / 1024d:0.#} KB";
    }
}

public static class CpuTopologyService
{
    public static CpuTopology Detect()
    {
        var groupCount = NativeMethods.GetActiveProcessorGroupCount();
        var logicalProcessorCount = GetLogicalProcessorCount(groupCount);
        var systemMask = GetSystemMask(logicalProcessorCount);

        var l3Caches = ReadL3Caches()
            .Where(cache => cache.Mask != 0)
            .Where(cache => (cache.Mask & ~systemMask) == 0)
            .GroupBy(cache => cache.Mask)
            .Select(group => new L3Cache(
                group.Key,
                group.Max(cache => cache.SizeBytes)))
            .OrderByDescending(cache => cache.SizeBytes)
            .ThenBy(cache => BitOperations.TrailingZeroCount(cache.Mask))
            .ToArray();

        var recommendedMask = l3Caches.FirstOrDefault()?.Mask ?? CreateFallbackMask(systemMask);
        if (recommendedMask == 0 || (recommendedMask & systemMask) == 0)
        {
            recommendedMask = CreateFallbackMask(systemMask);
        }

        var cacheSize = l3Caches.FirstOrDefault()?.SizeBytes ?? 0;

        return new CpuTopology(
            systemMask,
            recommendedMask,
            cacheSize,
            logicalProcessorCount,
            groupCount);
    }

    private static int GetLogicalProcessorCount(int groupCount)
    {
        var count = 0;
        for (ushort group = 0; group < groupCount; group++)
        {
            count += (int)NativeMethods.GetActiveProcessorCount(group);
        }

        return count;
    }

    private static ulong GetSystemMask(int logicalProcessorCount)
    {
        if (NativeMethods.GetProcessAffinityMask(
                NativeMethods.GetCurrentProcess(),
                out _,
                out var systemMask))
        {
            return systemMask.ToUInt64();
        }

        return CreateFallbackMask(logicalProcessorCount);
    }

    private static ulong CreateFallbackMask(ulong systemMask)
    {
        if (systemMask == 0)
        {
            return 0;
        }

        var processorCount = BitOperations.PopCount(systemMask);
        if (processorCount <= 16)
        {
            return systemMask;
        }

        // On the dual-CCD X3D processors, the large-cache CCD is normally the
        // first half. The cache query above is preferred; this only covers the
        // case where Windows does not expose useful L3 records.
        return CreateLowBitsMask(systemMask, Math.Min(16, processorCount));
    }

    private static ulong CreateFallbackMask(int logicalProcessorCount)
    {
        if (logicalProcessorCount <= 0)
        {
            return 0;
        }

        var count = Math.Min(logicalProcessorCount, 64);
        return count == 64 ? ulong.MaxValue : (1UL << count) - 1;
    }

    private static ulong CreateLowBitsMask(ulong sourceMask, int count)
    {
        var result = 0UL;
        var added = 0;

        for (var bit = 0; bit < 64 && added < count; bit++)
        {
            var value = 1UL << bit;
            if ((sourceMask & value) == 0)
            {
                continue;
            }

            result |= value;
            added++;
        }

        return result;
    }

    private static IReadOnlyList<L3Cache> ReadL3Caches()
    {
        var length = 0u;
        NativeMethods.GetLogicalProcessorInformationEx(
            NativeMethods.LogicalProcessorRelationship.RelationCache,
            IntPtr.Zero,
            ref length);

        if (length == 0 || Marshal.GetLastWin32Error() != NativeMethods.ErrorInsufficientBuffer)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!NativeMethods.GetLogicalProcessorInformationEx(
                    NativeMethods.LogicalProcessorRelationship.RelationCache,
                    buffer,
                    ref length))
            {
                return [];
            }

            var caches = new List<L3Cache>();
            var offset = 0;
            var totalLength = (int)length;

            while (offset + 8 <= totalLength)
            {
                var relationship = (NativeMethods.LogicalProcessorRelationship)
                    Marshal.ReadInt32(buffer, offset);
                var itemSize = Marshal.ReadInt32(buffer, offset + 4);

                if (itemSize < 8 || offset + itemSize > totalLength)
                {
                    break;
                }

                if (relationship == NativeMethods.LogicalProcessorRelationship.RelationCache)
                {
                    var cache = Marshal.PtrToStructure<NativeMethods.CacheRelationship>(
                        IntPtr.Add(buffer, offset + 8));

                    if (cache.Level == 3 &&
                        cache.Type == NativeMethods.ProcessorCacheType.CacheUnified &&
                        cache.GroupMask.Group == 0)
                    {
                        caches.Add(new L3Cache(cache.GroupMask.Mask.ToUInt64(), cache.CacheSize));
                    }
                }

                offset += itemSize;
            }

            return caches;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private sealed record L3Cache(ulong Mask, uint SizeBytes);
}
